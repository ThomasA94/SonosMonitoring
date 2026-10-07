namespace SonosMonitoring.Domain.Monitoring.Events;

/// <summary>Records the complete resulting monitoring policy so standard projections can fold it.</summary>
[EventContract("sonosmonitoring.device_monitoring.enabled", 1)]
public sealed class DeviceMonitoringEnabled : AggregateEvent<DeviceMonitoringEnabled, DeviceMonitoring, DeviceMonitoringId>
{
    /// <summary>Gets the stable Sonos device identity, never its mutable IP.</summary>
    public string DeviceKey { get; init; } = string.Empty;
    /// <summary>Gets whether monitoring is enabled after this event.</summary>
    public bool Enabled { get; init; }
    /// <summary>Gets the polling interval after this event, in seconds.</summary>
    public int IntervalSeconds { get; init; }
}
