using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SonosMonitoring.Application.Collection;

namespace SonosMonitoring.Infrastructure.History;

/// <summary>Samples bounded observational history without blocking discovery or turning observations into business commands.</summary>
public sealed class ObservationHistoryWorker : BackgroundService
{
    private readonly ObservationHistoryOptions _options;
    private readonly ICollectorSnapshotReader _collector;
    private readonly PostgresObservationHistory _history;
    private readonly ILogger<ObservationHistoryWorker> _logger;

    /// <summary>Connects the single collector to its independent journal.</summary>
    /// <param name="options">Sampling and retention limits.</param>
    /// <param name="collector">Detached current observation reader.</param>
    /// <param name="history">Atomic bounded journal writer.</param>
    /// <param name="logger">Receives bounded status only, never database credentials or device payloads.</param>
    public ObservationHistoryWorker(ObservationHistoryOptions options, ICollectorSnapshotReader collector,
        PostgresObservationHistory history, ILogger<ObservationHistoryWorker> logger)
    {
        this._options = options;
        this._collector = collector;
        this._history = history;
        this._logger = logger;
    }

    /// <summary>Validates an explicitly provisioned schema before accepting host traffic.</summary>
    /// <param name="cancellationToken">Host startup budget.</param>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await this._history.VerifyAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Guid runId = Guid.NewGuid();
        bool storageGap = false;
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                var snapshot = this._collector.GetSnapshot();
                var now = DateTimeOffset.UtcNow;
                bool observing = snapshot.Enabled && snapshot.HeartbeatUtc is { } heartbeat &&
                    now - heartbeat <= TimeSpan.FromSeconds(snapshot.HeartbeatMaxAgeSeconds);
                await this._history.WriteAsync(runId, now, observing,
                    snapshot.Devices.Select(ObservationNormalizer.Normalize).ToArray(), storageGap, stoppingToken);
                storageGap = false;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) when (exception is Npgsql.NpgsqlException or IOException or TimeoutException or OperationCanceledException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
            {
                if (storageGap == false)
                {
                    this._logger.LogWarning("Observation history unavailable; collection continues and a gap will be recorded after reconnection.");
                }
                storageGap = true;
            }
            await Task.Delay(storageGap ? TimeSpan.FromSeconds(10) : this._options.SampleInterval, stoppingToken);
        }
    }
}
