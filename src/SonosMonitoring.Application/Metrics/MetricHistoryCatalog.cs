namespace SonosMonitoring.Application.Metrics;

/// <summary>Defines the single allowlist and units for history selection.</summary>
public static class MetricHistoryCatalog
{
    /// <summary>All selectors exposed by the history API and its selection catalog.</summary>
    public static IReadOnlyList<MetricHistoryDefinition> Definitions { get; } =
    [
        new("receive_bytes", "bytes", "interface", "interface"),
        new("transmit_bytes", "bytes", "interface", "interface"),
        new("receive_rate", "bytes/second", "interface", "interface"),
        new("transmit_rate", "bytes/second", "interface", "interface"),
        new("probe_success", "boolean", "device", "device_id"),
        new("receive_packets", "packets", "interface", "interface"),
        new("transmit_packets", "packets", "interface", "interface"),
        new("receive_errors", "errors", "interface", "interface"),
        new("transmit_errors", "errors", "interface", "interface"),
        new("receive_dropped", "drops", "interface", "interface"),
        new("transmit_dropped", "drops", "interface", "interface"),
        new("receive_packets_rate", "packets/second", "interface", "interface"),
        new("transmit_packets_rate", "packets/second", "interface", "interface"),
        new("receive_errors_rate", "errors/second", "interface", "interface"),
        new("transmit_errors_rate", "errors/second", "interface", "interface"),
        new("receive_dropped_rate", "drops/second", "interface", "interface"),
        new("transmit_dropped_rate", "drops/second", "interface", "interface"),
        new("source_success", "boolean", "source", "source"),
        new("source_fresh", "boolean", "source", "source"),
        new("source_age", "seconds", "source", "source"),
        new("gena_subscribed", "boolean", "device", "device_id"),
        new("gena_initial_received", "boolean", "device", "device_id"),
        new("gena_last_notification_age", "seconds", "device", "device_id"),
        new("probe_duration", "seconds", "device", "device_id"),
        new("volume", "percent", "device", "device_id"),
        new("muted", "boolean", "device", "device_id"),
    ];

    /// <summary>Source labels emitted by the collector.</summary>
    public static IReadOnlySet<string> AllowedSources { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "description", "ifconfig", "topology", "wireless", "ethernet", "playback", "volume",
    };

    /// <summary>Finds an exposed definition by its stable selector.</summary>
    /// <param name="selector">Selector from an API request.</param>
    /// <returns>The matching definition, or null when the selector is not allowlisted.</returns>
    public static MetricHistoryDefinition? Find(string selector) =>
        Definitions.FirstOrDefault(definition => definition.Selector == selector);
}
