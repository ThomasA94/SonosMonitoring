namespace SonosMonitoring.Contracts.Monitoring;

/// <summary>Requests deactivation of an existing policy.</summary>
/// <param name="ExpectedToken">Concurrency token returned by its last confirmed commit.</param>
public sealed record DisableMonitoringRequest(string ExpectedToken);
