namespace SonosMonitoring.Platform;

/// <summary>Stable event store and routing names for one media control operation.</summary>
public static class MediaControlNames
{
    /// <summary>Aggregate name retained for the lifetime of operation streams.</summary>
    public const string AGGREGATE = "MediaOperation";
    /// <summary>Durable event routing address.</summary>
    public const string STREAM = "sonosmonitoring.media-control.events";
    /// <summary>Routing definition identifier.</summary>
    public const string ROUTE = "media-control";
    /// <summary>Shared logical event store.</summary>
    public const string STORE = MonitoringNames.STORE;
    /// <summary>Shared logical event bus.</summary>
    public const string BUS = MonitoringNames.BUS;
    /// <summary>Installation tenant.</summary>
    public const string TENANT = MonitoringNames.TENANT;
}
