using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SonosMonitoring.Application.MediaControl;

namespace SonosMonitoring.Infrastructure.MediaControl;

/// <summary>Polls the authoritative queue and runs disjoint speaker operations under one database owner.</summary>
public sealed class MediaControlWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IMediaCoordinationStore _store;
    private readonly MediaControlOptions _options;
    private readonly ILogger<MediaControlWorker> _logger;

    /// <summary>Receives isolated execution scopes and one installation's durable coordinator.</summary>
    /// <param name="scopes">Domain executor scope factory.</param>
    /// <param name="store">Authoritative execution queue.</param>
    /// <param name="options">Bounded dispatch settings.</param>
    /// <param name="logger">Payload-free technical diagnostics.</param>
    public MediaControlWorker(IServiceScopeFactory scopes, IMediaCoordinationStore store, MediaControlOptions options, ILogger<MediaControlWorker> logger)
    {
        this._scopes = scopes;
        this._store = store;
        this._options = options;
        this._logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                IAsyncDisposable? ownership = await this._store.AcquireExecutorAsync(stoppingToken);
                if (ownership is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                await using (ownership)
                {
                    using CancellationTokenSource executorLifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    Dictionary<Guid, ActiveExecution> active = [];
                    try
                    {
                        await this._store.RecoverAsync(stoppingToken);
                        while (await this._store.OwnsExecutorAsync(stoppingToken))
                        {
                            await this.SynchronizeUnrecordedAsync(active.Keys.ToHashSet(), stoppingToken);
                            await this.ScheduleReadyAsync(active, executorLifetime.Token);
                            await Task.Delay(TimeSpan.FromMilliseconds(300), stoppingToken);
                        }
                    }
                    finally
                    {
                        try
                        {
                            executorLifetime.Cancel();
                        }
                        catch (Exception exception)
                        {
                            this._logger.LogWarning("Media executor cancellation callback failed ({FailureType}).", exception.GetType().Name);
                        }
                        await this.DrainAsync(active);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Never log exception messages: XML and connection strings can contain sensitive source data.
                this._logger.LogWarning("Media executor unavailable ({FailureType}); queued work remains fenced.", exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }

    /// <summary>Records bounded technical outcomes before admitting more operations.</summary>
    /// <param name="activeIds">Operations whose command sequences are still owned by active tasks.</param>
    /// <param name="ct">Executor ownership lifetime.</param>
    private async Task SynchronizeUnrecordedAsync(IReadOnlySet<Guid> activeIds, CancellationToken ct)
    {
        foreach (CoordinatedMediaOperation outcome in await this._store.UnrecordedAsync(ct))
        {
            // Revocation may be visible while a device task still owns its domain transitions.
            if (activeIds.Contains(outcome.OperationId))
            {
                continue;
            }
            using var auditScope = this._scopes.CreateScope();
            using var auditBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            auditBudget.CancelAfter(TimeSpan.FromSeconds(10));
            await auditScope.ServiceProvider.GetRequiredService<MediaExecutionService>().SynchronizeAsync(outcome, auditBudget.Token);
        }
    }

    /// <summary>Starts ready, non-overlapping work without waiting for already running device requests.</summary>
    /// <param name="active">Current task and target reservations.</param>
    /// <param name="ct">Executor ownership lifetime.</param>
    private async Task ScheduleReadyAsync(Dictionary<Guid, ActiveExecution> active, CancellationToken ct)
    {
        await ObserveCompletedAsync(active);
        if (active.Count >= this._options.MaxConcurrentExecutions)
        {
            return;
        }

        IReadOnlyList<CoordinatedMediaOperation> pending = await this._store.PendingAsync(ct);
        IReadOnlySet<Guid> activeIds = active.Keys.ToHashSet();
        IReadOnlyList<CoordinatedMediaOperation> selected = await MediaExecutionScheduler.SelectAsync(
            pending,
            active.Values.Select(execution => execution.Targets),
            activeIds,
            this._options.MaxConcurrentExecutions - active.Count,
            (operation, token) => this._store.IsReadyAsync(operation, token),
            ct);
        foreach (CoordinatedMediaOperation operation in selected)
        {
            CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(this._options.ExecutionSeconds));
            Task execution = this.ExecuteOperationAsync(operation, deadline.Token);
            active.Add(operation.OperationId, new ActiveExecution(operation.Targets, deadline, execution));
        }
    }

    /// <summary>Runs one operation in an isolated dependency scope.</summary>
    /// <param name="operation">Admitted operation descriptor.</param>
    /// <param name="ct">Ownership and execution deadline token.</param>
    private async Task ExecuteOperationAsync(CoordinatedMediaOperation operation, CancellationToken ct)
    {
        using IServiceScope scope = this._scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MediaExecutionService>().ProcessAsync(operation, ct);
    }

    /// <summary>Releases completed target reservations and propagates unexpected operation failures.</summary>
    /// <param name="active">Current task and target reservations.</param>
    private static async Task ObserveCompletedAsync(Dictionary<Guid, ActiveExecution> active)
    {
        foreach ((Guid id, ActiveExecution execution) in active.ToArray())
        {
            if (execution.Task.IsCompleted == false)
            {
                continue;
            }

            active.Remove(id);
            execution.Deadline.Dispose();
            await execution.Task;
        }
    }

    /// <summary>Cancels active device work and waits until every operation has released its resources.</summary>
    /// <param name="active">Current task and target reservations.</param>
    private async Task DrainAsync(Dictionary<Guid, ActiveExecution> active)
    {
        foreach (ActiveExecution execution in active.Values)
        {
            try
            {
                execution.Deadline.Cancel();
            }
            catch (Exception exception)
            {
                this._logger.LogWarning("Media execution cancellation callback failed ({FailureType}).", exception.GetType().Name);
            }
        }

        try
        {
            await Task.WhenAll(active.Values.Select(execution => execution.Task));
        }
        catch (Exception exception)
        {
            // Each task is observed before ownership is released, including failures during cancellation cleanup.
            this._logger.LogWarning("Media execution drain completed with {FailureType}.", exception.GetType().Name);
        }
        finally
        {
            foreach (ActiveExecution execution in active.Values)
            {
                execution.Deadline.Dispose();
            }
            active.Clear();
        }
    }

    /// <summary>Tracks a running task and the target set reserved until that task drains.</summary>
    /// <param name="Targets">Targets reserved by the operation.</param>
    /// <param name="Deadline">Cancellation source for ownership loss, shutdown, and execution timeout.</param>
    /// <param name="Task">Operation task owned by the worker.</param>
    private sealed record ActiveExecution(string[] Targets, CancellationTokenSource Deadline, Task Task);
}
