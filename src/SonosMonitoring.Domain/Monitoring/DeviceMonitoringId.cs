namespace SonosMonitoring.Domain.Monitoring;

/// <summary>Identifies one device's monitoring policy independently of its current network address.</summary>
public sealed class DeviceMonitoringId : AggregateIdentity<DeviceMonitoringId>
{
    /// <summary>Creates an identity from its persisted canonical representation.</summary>
    /// <param name="value">The canonical identity.</param>
    public DeviceMonitoringId(string value) : base(value) { }
}
