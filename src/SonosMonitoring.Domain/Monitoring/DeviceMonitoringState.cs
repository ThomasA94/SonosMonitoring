namespace SonosMonitoring.Domain.Monitoring;

/// <summary>Immutable policy state; observations and transport addresses are deliberately excluded.</summary>
public sealed class DeviceMonitoringState : Entity<DeviceMonitoringId>
{
    /// <summary>Constructs the complete state, including snapshot deserialization.</summary>
    /// <param name="id">The stable policy identity.</param>
    /// <param name="deviceKey">The stable Sonos UDN without the uuid prefix.</param>
    /// <param name="enabled">Whether the operator enabled monitoring.</param>
    /// <param name="intervalSeconds">The requested polling interval.</param>
    public DeviceMonitoringState(DeviceMonitoringId id, string deviceKey = "", bool enabled = false, int intervalSeconds = 15) : base(id)
    {
        this.DeviceKey = deviceKey;
        this.Enabled = enabled;
        this.IntervalSeconds = intervalSeconds;
    }

    /// <summary>Gets the immutable identity of the monitored device.</summary>
    public string DeviceKey { get; }
    /// <summary>Gets whether monitoring is enabled.</summary>
    public bool Enabled { get; }
    /// <summary>Gets the configured interval, in seconds.</summary>
    public int IntervalSeconds { get; }
}
