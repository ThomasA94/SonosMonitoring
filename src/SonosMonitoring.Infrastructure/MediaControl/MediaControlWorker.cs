using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SonosMonitoring.Application.MediaControl;

namespace SonosMonitoring.Infrastructure.MediaControl;

/// <summary>Polls the authoritative queue; bus replay or projection rebuild never drives speaker effects.</summary>
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
                await using var ownership = await this._store.AcquireExecutorAsync(stoppingToken);
                if (ownership is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }
                await this._store.RecoverAsync(stoppingToken);
                while (await this._store.OwnsExecutorAsync(stoppingToken))
                {
                    foreach (var outcome in await this._store.UnrecordedAsync(stoppingToken))
                    {
                        using var auditScope = this._scopes.CreateScope();
                        using var auditBudget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        auditBudget.CancelAfter(TimeSpan.FromSeconds(10));
                        await auditScope.ServiceProvider.GetRequiredService<MediaExecutionService>().SynchronizeAsync(outcome, auditBudget.Token);
                    }
                    foreach (var operation in await this._store.PendingAsync(stoppingToken))
                    {
                        using var scope = this._scopes.CreateScope();
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        deadline.CancelAfter(TimeSpan.FromSeconds(this._options.ExecutionSeconds));
                        await scope.ServiceProvider.GetRequiredService<MediaExecutionService>().ProcessAsync(operation, deadline.Token);
                    }
                    await Task.Delay(TimeSpan.FromMilliseconds(300), stoppingToken);
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
}
