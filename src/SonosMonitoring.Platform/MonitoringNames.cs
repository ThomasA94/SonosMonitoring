namespace SonosMonitoring.Platform;

/// <summary>Defines stable persistence and transport names shared by the backend hosts.</summary>
public static class MonitoringNames
{
    /// <summary>The canonical aggregate name; changing it requires a stream migration.</summary>
    public const string AGGREGATE = "DeviceMonitoring";
    /// <summary>The durable event routing address.</summary>
    public const string STREAM = "sonosmonitoring.device-monitoring.events";
    /// <summary>The routing definition identifier.</summary>
    public const string ROUTE = "device-monitoring";
    /// <summary>The logical event store identifier.</summary>
    public const string STORE = "sonosmonitoring";
    /// <summary>The logical event bus identifier.</summary>
    public const string BUS = "sonosmonitoring";
    /// <summary>The fixed tenant of this single-installation backend.</summary>
    public const string TENANT = "SonosMonitoring";
}
