using Devbone.Frameworks.EventSourcing.Ddd.Routing;
using SonosMonitoring.Platform;

namespace SonosMonitoring.Infrastructure.Monitoring;

/// <summary>Builds the single authoritative routing definition used by commits and the event catalogue.</summary>
public static class MonitoringRouting
{
    /// <summary>Creates the versioned monitoring route without performing any I/O.</summary>
    public static AggregateRoutingOptions Create() => new()
    {
        StreamNamingConvention = @"^sonosmonitoring\.[a-z-]+\.events$",
        Aggregates = { [MonitoringNames.AGGREGATE] = MonitoringNames.ROUTE, [MediaControlNames.AGGREGATE] = MediaControlNames.ROUTE },
        Routes =
        {
            [MediaControlNames.ROUTE] = new AggregateRouteDefinitionOptions
            {
                CurrentVersion = 1,
                Versions =
                {
                    [1] = new AggregateRouteTargetOptions
                    {
                        EventStoreId = MonitoringNames.STORE, EventBusId = MonitoringNames.BUS, Stream = MediaControlNames.STREAM,
                    },
                },
            },
            [MonitoringNames.ROUTE] = new AggregateRouteDefinitionOptions
            {
                CurrentVersion = 1,
                Versions =
                {
                    [1] = new AggregateRouteTargetOptions
                    {
                        EventStoreId = MonitoringNames.STORE, EventBusId = MonitoringNames.BUS, Stream = MonitoringNames.STREAM,
                    },
                },
            },
        },
    };
}
