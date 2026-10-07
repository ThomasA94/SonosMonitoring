namespace SonosMonitoring.Contracts.Monitoring;

/// <summary>Requests activation for the device named by the route.</summary>
/// <param name="IntervalSeconds">Polling interval, 5 to 300 seconds.</param>
/// <param name="ExpectedToken">Previous commit token; null requires a new policy.</param>
public sealed record EnableMonitoringRequest(int IntervalSeconds, string? ExpectedToken = null);
