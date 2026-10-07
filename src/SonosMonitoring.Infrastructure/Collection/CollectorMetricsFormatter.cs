using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SonosMonitoring.Application.Collection;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Formats bounded collector snapshots as Prometheus text exposition.</summary>
public static class CollectorMetricsFormatter
{
    /// <summary>Declares every metric emitted by this formatter.</summary>
    public static IReadOnlyList<CollectorMetricDefinition> Catalog { get; } =
    [
        new("sonos_media_configured", "gauge", "Whether this API deployment provides the media coordination slice."),
        new("sonos_media_coordination_available", "gauge", "Whether the durable media coordination store is available at scrape time."),
        new("sonos_media_operations", "gauge", "Retained media operations by bounded execution phase; not a count of device successes."),
        new("sonos_collector_enabled", "gauge", "Whether background collection is enabled by configuration."),
        new("sonos_collector_heartbeat_max_age_seconds", "gauge", "Allowed heartbeat age including sweep, poll and scheduling budgets."),
        new("sonos_collector_heartbeat_timestamp_seconds", "gauge", "Unix time of the latest collector heartbeat."),
        new("sonos_collector_sweep_duration_seconds", "gauge", "Duration of the latest completed bounded collection sweep."),
        new("sonos_collector_probe_attempts_total", "counter", "Description probes in this collector generation, including discovery addresses."),
        new("sonos_collector_probe_failures_total", "counter", "Failed description probes, including expected discovery misses."),
        new("sonos_collector_probe_timeouts_total", "counter", "Timed-out description probes in this collector generation."),
        new("sonos_discovery_addresses_probed", "gauge", "Unique addresses actually probed during the latest full discovery pass."),
        new("sonos_discovery_devices_found", "gauge", "Successful validated description probes during the latest deduplicated discovery pass."),
        new("sonos_device_info", "gauge", "Last known readable player metadata; value is 1, not evidence of current reachability."),
        new("sonos_wireless_info", "gauge", "Current reported wireless mode and connection metadata; absent when the source is unavailable."),
        new("sonos_playback_info", "gauge", "Current reported transport state and status; absent when the source is unavailable."),
        new("sonos_device_probe_success", "gauge", "Whether the latest device description probe succeeded."),
        new("sonos_device_probe_duration_seconds", "gauge", "Duration of the latest description probe including bounded retries."),
        new("sonos_device_monitoring_enabled", "gauge", "Latest confirmed diagnostic scheduling permission; absent if unavailable."),
        new("sonos_device_diagnostic_interval_seconds", "gauge", "Effective diagnostic polling interval including the collector floor."),
        new("sonos_device_gena_enabled", "gauge", "Whether topology callback collection is enabled."),
        new("sonos_device_gena_subscribed", "gauge", "Whether a topology callback lease is currently subscribed."),
        new("sonos_device_gena_initial_received", "gauge", "Whether the topology callback initial notification was confirmed."),
        new("sonos_device_gena_last_notification_timestamp_seconds", "gauge", "Unix time of the last topology notification, absent before any notification."),
        new("sonos_device_volume", "gauge", "Player Master volume on its reported 0 to 100 scale; not acoustic loudness."),
        new("sonos_device_muted", "gauge", "Player Master mute state, independent of group mute."),
        new("sonos_ethernet_link_up", "gauge", "Reported link state for an Ethernet port; not overall device reachability."),
        new("sonos_source_scrape_success", "gauge", "Whether the latest source attempt succeeded."),
        new("sonos_source_observation_fresh", "gauge", "Whether the latest source attempt succeeded and its last good observation is current."),
        new("sonos_source_last_success_timestamp_seconds", "gauge", "Unix time of the latest successful source observation."),
        new("sonos_interface_receive_bytes_total", "counter", "Raw receive bytes reported by the device interface."),
        new("sonos_interface_transmit_bytes_total", "counter", "Raw transmit bytes reported by the device interface."),
        new("sonos_interface_receive_packets_total", "counter", "Raw receive packets reported by the device interface."),
        new("sonos_interface_transmit_packets_total", "counter", "Raw transmit packets reported by the device interface."),
        new("sonos_interface_receive_errors_total", "counter", "Raw receive errors reported by the device interface."),
        new("sonos_interface_transmit_errors_total", "counter", "Raw transmit errors reported by the device interface."),
        new("sonos_interface_receive_dropped_total", "counter", "Raw receive drops reported by the device interface."),
        new("sonos_interface_transmit_dropped_total", "counter", "Raw transmit drops reported by the device interface.")
    ];

    /// <summary>Builds a complete scrape from one read-only snapshot.</summary>
    /// <param name="snapshot">Collector state copied at request time.</param>
    /// <returns>Prometheus text exposition, without stale numeric samples.</returns>
    public static string Format(CollectorSnapshot snapshot)
    {
        StringBuilder output = new();
        foreach (CollectorMetricDefinition metric in Catalog)
        {
            output.Append("# HELP ").Append(metric.Name).Append(' ').Append(metric.Help).Append('\n');
            output.Append("# TYPE ").Append(metric.Name).Append(' ').Append(metric.Type).Append('\n');
        }

        Append(output, "sonos_collector_enabled", string.Empty, snapshot.Enabled ? 1 : 0);
        Append(output, "sonos_collector_heartbeat_max_age_seconds", string.Empty, snapshot.HeartbeatMaxAgeSeconds);
        Append(output, "sonos_collector_probe_attempts_total", string.Empty, snapshot.ProbeAttemptsTotal);
        Append(output, "sonos_collector_probe_failures_total", string.Empty, snapshot.ProbeFailuresTotal);
        Append(output, "sonos_collector_probe_timeouts_total", string.Empty, snapshot.ProbeTimeoutsTotal);
        Append(output, "sonos_discovery_addresses_probed", string.Empty, (long)snapshot.DiscoveryAddressesProbed);
        Append(output, "sonos_discovery_devices_found", string.Empty, (long)snapshot.DiscoveryDevicesFound);
        if (snapshot.SweepDurationSeconds is double sweepDuration)
        {
            Append(output, "sonos_collector_sweep_duration_seconds", string.Empty, sweepDuration);
        }
        if (snapshot.HeartbeatUtc is DateTimeOffset heartbeat)
        {
            Append(output, "sonos_collector_heartbeat_timestamp_seconds", string.Empty, heartbeat.ToUnixTimeSeconds());
        }

        IReadOnlyDictionary<string, string> deviceNames = CollectorDeviceMetadata.Names(snapshot.Devices);
        foreach (DeviceSnapshot device in snapshot.Devices)
        {
            string deviceLabel = $"device_id=\"{DeviceLabel(device.DeviceId)}\"";
            Append(output, "sonos_device_info", deviceLabel +
                MetadataLabel("device_name", deviceNames[device.DeviceId]) +
                MetadataLabel("room_name", device.RoomName) +
                MetadataLabel("model_name", device.ModelName) +
                MetadataLabel("model_number", device.ModelNumber) +
                MetadataLabel("software_version", device.SoftwareVersion) +
                MetadataLabel("hardware_version", device.HardwareVersion), 1L);
            if (Current(device.WirelessStatus) && device.Wireless is { } wireless)
            {
                Append(output, "sonos_wireless_info", deviceLabel +
                    MetadataLabel("wifi_mode", wireless.WifiMode) +
                    MetadataLabel("connection_type", wireless.ConnectionType) +
                    MetadataLabel("sonosnet_disabled", wireless.SonosNetDisabled switch
                    {
                        true => "true",
                        false => "false",
                        null => "unknown"
                    }), 1L);
            }
            if (Current(device.PlaybackStatus) && device.Playback is { } playback)
            {
                Append(output, "sonos_playback_info", deviceLabel +
                    MetadataLabel("transport_state", playback.TransportState) +
                    MetadataLabel("transport_status", playback.TransportStatus), 1L);
            }
            if (device.Description.LastAttemptUtc is not null)
            {
                Append(output, "sonos_device_probe_success", deviceLabel, device.Description.LastAttemptSucceeded ? 1 : 0);
            }

            AppendSource(output, deviceLabel, device.Description);
            AppendSource(output, deviceLabel, device.InterfaceStatus);
            AppendSource(output, deviceLabel, device.TopologyStatus);
            AppendSource(output, deviceLabel, device.WirelessStatus);
            AppendSource(output, deviceLabel, device.EthernetStatus);
            AppendSource(output, deviceLabel, device.PlaybackStatus);
            AppendSource(output, deviceLabel, device.VolumeStatus);
            if (device.MonitoringEnabled is bool monitoring)
            {
                Append(output, "sonos_device_monitoring_enabled", deviceLabel, monitoring ? 1 : 0);
            }
            if (device.EffectiveIntervalSeconds is double interval)
            {
                Append(output, "sonos_device_diagnostic_interval_seconds", deviceLabel, interval);
            }
            if (device.ProbeDurationSeconds is double duration)
            {
                Append(output, "sonos_device_probe_duration_seconds", deviceLabel, duration);
            }
            Append(output, "sonos_device_gena_enabled", deviceLabel, device.Gena.Enabled ? 1 : 0);
            Append(output, "sonos_device_gena_subscribed", deviceLabel, device.Gena.Subscribed ? 1 : 0);
            Append(output, "sonos_device_gena_initial_received", deviceLabel, device.Gena.InitialNotificationReceived ? 1 : 0);
            if (device.Gena.LastNotificationUtc is DateTimeOffset notification)
            {
                Append(output, "sonos_device_gena_last_notification_timestamp_seconds", deviceLabel, notification.ToUnixTimeSeconds());
            }
            if (Current(device.VolumeStatus))
            {
                if (device.Volume is int volume)
                {
                    Append(output, "sonos_device_volume", deviceLabel, (long)volume);
                }
                if (device.Muted is bool muted)
                {
                    Append(output, "sonos_device_muted", deviceLabel, muted ? 1 : 0);
                }
            }
            if (Current(device.EthernetStatus))
            {
                foreach (var port in device.EthernetPorts)
                {
                    Append(output, "sonos_ethernet_link_up", deviceLabel + ",port=\"" + Escape(port.Port) + "\"", port.LinkUp ? 1 : 0);
                }
            }
            if (device.InterfaceStatus.IsStale ||
                device.InterfaceStatus.ErrorCode is "monitoring_disabled" or "policy_unavailable")
            {
                continue;
            }

            foreach (InterfaceCounters item in device.Interfaces)
            {
                string labels = $"{deviceLabel},interface=\"{Escape(item.Interface)}\"";
                Append(output, "sonos_interface_receive_bytes_total", labels, item.ReceiveBytes);
                Append(output, "sonos_interface_transmit_bytes_total", labels, item.TransmitBytes);
                Append(output, "sonos_interface_receive_packets_total", labels, item.ReceivePackets);
                Append(output, "sonos_interface_transmit_packets_total", labels, item.TransmitPackets);
                Append(output, "sonos_interface_receive_errors_total", labels, item.ReceiveErrors);
                Append(output, "sonos_interface_transmit_errors_total", labels, item.TransmitErrors);
                Append(output, "sonos_interface_receive_dropped_total", labels, item.ReceiveDropped);
                Append(output, "sonos_interface_transmit_dropped_total", labels, item.TransmitDropped);
            }
        }

        return output.ToString();
    }

    /// <summary>Allows numeric state export only after a successful current observation.</summary>
    /// <param name="source">Independent source evidence.</param>
    private static bool Current(SourceSnapshot source) => source.LastAttemptSucceeded && source.LastSuccessUtc is not null &&
        source.IsStale == false && source.ErrorCode is null;

    /// <summary>Appends one bounded printable metadata label with Prometheus escaping.</summary>
    /// <param name="name">Fixed metadata label name.</param>
    /// <param name="value">Optional untrusted player text.</param>
    /// <returns>A safe label fragment preceded by its separator.</returns>
    private static string MetadataLabel(string name, string? value) =>
        $",{name}=\"{Escape(CollectorDeviceMetadata.Text(value))}\"";

    /// <summary>Appends a finite measured duration or interval using invariant decimal formatting.</summary>
    /// <param name="output">Destination text.</param>
    /// <param name="name">Declared metric name.</param>
    /// <param name="labels">Already escaped labels.</param>
    /// <param name="value">Measured finite value.</param>
    private static void Append(StringBuilder output, string name, string labels, double value)
    {
        if (double.IsFinite(value) == false || value < 0)
        {
            return;
        }
        output.Append(name);
        if (labels.Length > 0)
        {
            output.Append('{').Append(labels).Append('}');
        }
        output.Append(' ').Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
    }

    /// <summary>Emits per-source health and last success without repeating sensitive metadata.</summary>
    /// <param name="output">Destination text.</param>
    /// <param name="deviceLabel">Pseudonymized device label.</param>
    /// <param name="source">Source status.</param>
    private static void AppendSource(StringBuilder output, string deviceLabel, SourceSnapshot source)
    {
        string labels = $"{deviceLabel},source=\"{Escape(source.Name)}\"";
        Append(output, "sonos_source_observation_fresh", labels,
            source.LastAttemptSucceeded && source.LastSuccessUtc is not null && source.IsStale == false ? 1 : 0);
        if (source.LastAttemptUtc is not null)
        {
            Append(output, "sonos_source_scrape_success", labels, source.LastAttemptSucceeded ? 1 : 0);
        }

        if (source.LastSuccessUtc is DateTimeOffset lastSuccess)
        {
            Append(output, "sonos_source_last_success_timestamp_seconds", labels, lastSuccess.ToUnixTimeSeconds());
        }
    }

    /// <summary>Appends one numerical sample in invariant decimal format.</summary>
    /// <param name="output">Destination text.</param>
    /// <param name="name">Declared metric name.</param>
    /// <param name="labels">Already escaped label string, or empty.</param>
    /// <param name="value">Raw numerical value.</param>
    private static void Append(StringBuilder output, string name, string labels, ulong value)
    {
        output.Append(name);
        if (labels.Length > 0)
        {
            output.Append('{').Append(labels).Append('}');
        }

        output.Append(' ').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    /// <summary>Appends one signed timestamp or status sample.</summary>
    /// <param name="output">Destination text.</param>
    /// <param name="name">Declared metric name.</param>
    /// <param name="labels">Already escaped label string, or empty.</param>
    /// <param name="value">Numerical value.</param>
    private static void Append(StringBuilder output, string name, string labels, long value)
    {
        output.Append(name);
        if (labels.Length > 0)
        {
            output.Append('{').Append(labels).Append('}');
        }

        output.Append(' ').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    /// <summary>Converts a UDN into a stable, bounded label that does not expose the raw identifier.</summary>
    /// <param name="deviceId">Stable device UDN.</param>
    /// <returns>First 128 bits of its SHA-256 digest as lowercase hex.</returns>
    public static string DeviceLabel(string deviceId)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(deviceId));
        return Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
    }

    /// <summary>Escapes a bounded Prometheus label value.</summary>
    /// <param name="value">Untrusted label text.</param>
    /// <returns>Escaped text safe inside double quotes.</returns>
    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
