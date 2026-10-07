namespace SonosMonitoring.Application.Monitoring;

/// <summary>Represents a projected monitoring policy and its explicit event watermark.</summary>
/// <param name="AggregateId">The durable policy stream.</param>
/// <param name="DeviceKey">The monitored device.</param>
/// <param name="Enabled">The resulting activation state.</param>
/// <param name="IntervalSeconds">The polling interval.</param>
/// <param name="EventSequence">Last projected event in this stream.</param>
/// <param name="UpdatedAtUtc">Time the projection was last written.</param>
public sealed record MonitoringView(string AggregateId, string DeviceKey, bool Enabled, int IntervalSeconds, long EventSequence, DateTimeOffset UpdatedAtUtc);
