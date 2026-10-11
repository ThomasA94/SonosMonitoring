using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SonosMonitoring.Application.MediaControl;
using SonosMonitoring.Domain.MediaControl;

namespace SonosMonitoring.Infrastructure.MediaControl;

/// <summary>Reconciles unadmitted event streams after outages and restarts, without performing device I/O.</summary>
public sealed class MediaAdmissionRecoveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IMediaAdmissionCandidates _candidates;
    private readonly ILogger<MediaAdmissionRecoveryWorker> _logger;

    /// <summary>Receives bounded projection discovery and scoped authoritative reconciliation.</summary>
    /// <param name="scopes">Independent command scopes.</param>
    /// <param name="candidates">Rebuildable discovery hints.</param>
    /// <param name="logger">Safe operational diagnostics.</param>
    public MediaAdmissionRecoveryWorker(IServiceScopeFactory scopes, IMediaAdmissionCandidates candidates,
        ILogger<MediaAdmissionRecoveryWorker> logger)
    {
        this._scopes = scopes;
        this._candidates = candidates;
        this._logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? after = null;
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                budget.CancelAfter(TimeSpan.FromSeconds(10));
                var page = await this._candidates.ReadAsync(after, budget.Token);
                foreach (string candidate in page)
                {
                    // Only canonical media identities can become authoritative lookup candidates.
                    if (candidate.Length >= 36 && Guid.TryParseExact(candidate[^36..], "D", out Guid id) &&
                        MediaOperationId.With(id).ToString() == candidate)
                    {
                        using var scope = this._scopes.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<MediaAdmissionReconciler>().ReconcileAsync(id, budget.Token);
                    }
                    after = candidate;
                }
                if (page.Count == 0)
                {
                    after = null;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                this._logger.LogWarning("Media admission reconciliation unavailable ({Kind}); retrying.", exception.GetType().Name);
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
