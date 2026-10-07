namespace SonosMonitoring.Application.Monitoring;

/// <summary>Reads eventually consistent policy views without loading write-side aggregates.</summary>
public interface IMonitoringQuery
{
    /// <summary>Reads the current projected position for a device, or null before projection.</summary>
    /// <param name="deviceKey">The stable Sonos key.</param>
    /// <param name="ct">Request cancellation.</param>
    Task<MonitoringView?> FindAsync(string deviceKey, CancellationToken ct);
}
