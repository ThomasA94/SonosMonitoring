using SonosMonitoring.Application.Collection.Diagnostics;

namespace SonosMonitoring.Application.Collection;

/// <summary>Creates a detached diagnostic view with explicit field selection and export-local identity aliases.</summary>
public static class CollectorSnapshotRedactor
{
    /// <summary>Preserves observations and reference relationships while removing raw identifiers and device-supplied free text.</summary>
    /// <param name="snapshot">One captured collector view; no network requests or cache mutations occur.</param>
    /// <returns>A separate view whose quality flags describe the original capture, not the later time of reading.</returns>
    public static CollectorSnapshot Redact(CollectorSnapshot snapshot)
    {
        ExportAliases aliases = new();
        return new CollectorSnapshot(snapshot.Enabled, snapshot.HeartbeatUtc, snapshot.LastSweepUtc,
            snapshot.Devices.Select(device => new DeviceSnapshot(aliases.Get("device", device.DeviceId),
                aliases.Get("address", device.Address), aliases.Optional("room", device.RoomName), null,
                device.FirstSeenUtc, device.LastSeenUtc, Source(device.Description, "description"),
                Source(device.InterfaceStatus, "ifconfig"), device.Interfaces.Select(item => new InterfaceCounters(
                    aliases.Get("interface", item.Interface), item.ReceiveBytes, item.TransmitBytes,
                    item.ReceivePackets, item.TransmitPackets, item.ReceiveErrors, item.TransmitErrors,
                    item.ReceiveDropped, item.TransmitDropped)).ToArray())
            {
                MonitoringEnabled = device.MonitoringEnabled,
                EffectiveIntervalSeconds = device.EffectiveIntervalSeconds,
                ProbeDurationSeconds = device.ProbeDurationSeconds,
                InterfaceRates = device.InterfaceRates.Select(rate => new InterfaceRate(aliases.Get("interface", rate.Interface),
                    rate.ReceiveBytesPerSecond, rate.TransmitBytesPerSecond, rate.SampledAtUtc, rate.IntervalSeconds)).ToArray(),
                WirelessStatus = Source(device.WirelessStatus, "wireless"),
                EthernetStatus = Source(device.EthernetStatus, "ethernet"),
                PlaybackStatus = Source(device.PlaybackStatus, "playback"),
                VolumeStatus = Source(device.VolumeStatus, "volume"),
                Wireless = device.Wireless is null ? null : new WirelessObservation(
                    SafeState(device.Wireless.WifiMode), SafeState(device.Wireless.ConnectionType), device.Wireless.SonosNetDisabled),
                EthernetPorts = device.EthernetPorts.Select(port => new EthernetPortObservation(aliases.Get("port", port.Port), port.LinkUp, port.ReportedSpeed)).ToArray(),
                Playback = device.Playback is null ? null : new PlaybackObservation(SafeState(device.Playback.TransportState) ?? "UNKNOWN",
                    SafeState(device.Playback.TransportStatus)),
                Volume = device.Volume,
                Muted = device.Muted,
                Gena = new GenaSnapshot(device.Gena.Enabled, device.Gena.Subscribed, device.Gena.LastAttemptUtc,
                    device.Gena.ExpiresUtc, device.Gena.LastNotificationUtc, device.Gena.InitialNotificationReceived, Error(device.Gena.ErrorCode)),
                TopologyStatus = Source(device.TopologyStatus, "topology"),
                Groups = device.Groups.Select(group => new TopologyGroup(aliases.Get("group", group.GroupId),
                    aliases.Get("device", group.CoordinatorDeviceId), group.Members.Select(member => new TopologyMember(
                        aliases.Get("device", member.DeviceId), aliases.Optional("room", member.RoomName),
                        member.IsInvisible, aliases.Optional("device", member.ParentDeviceId))
                    {
                        ReportedAddress = aliases.Optional("address", member.ReportedAddress)
                    }).ToArray())).ToArray()
            }).ToArray(), snapshot.Ranges.Select(range => new DiscoveryRangeSnapshot(aliases.Get("range", range.Cidr),
                range.LastAttemptUtc, range.LastCompletedUtc, range.LastAttemptSucceeded, range.AddressesProbed,
                range.DevicesFound, Error(range.ErrorCode))).ToArray(), snapshot.CapacityExceeded)
        {
            HeartbeatMaxAgeSeconds = snapshot.HeartbeatMaxAgeSeconds,
            SweepDurationSeconds = snapshot.SweepDurationSeconds,
            ProbeAttemptsTotal = snapshot.ProbeAttemptsTotal,
            ProbeFailuresTotal = snapshot.ProbeFailuresTotal,
            ProbeTimeoutsTotal = snapshot.ProbeTimeoutsTotal,
            DiscoveryAddressesProbed = snapshot.DiscoveryAddressesProbed,
            DiscoveryDevicesFound = snapshot.DiscoveryDevicesFound,
            Ssdp = new SsdpSnapshot(snapshot.Ssdp.Enabled, snapshot.Ssdp.LastAttemptUtc, snapshot.Ssdp.LastCompletedUtc,
                snapshot.Ssdp.CandidateCount, snapshot.Ssdp.FailedInterfaces, snapshot.Ssdp.LimitReached, Error(snapshot.Ssdp.ErrorCode)),
            TopologyDiscovery = new TopologyDiscoverySnapshot(snapshot.TopologyDiscovery.LastSelectionUtc,
                snapshot.TopologyDiscovery.CandidateCount, snapshot.TopologyDiscovery.LimitReached)
        };
    }

    /// <summary>Reduces device-supplied state strings to reviewed non-sensitive values.</summary>
    /// <param name="value">Reported state string.</param>
    private static string? SafeState(string? value) => value switch
    {
        null => null,
        "STATION_MODE" or "AP_MODE" or "INFRA" or "WiFi" or "Ethernet" or "SonosNet" or
        "PLAYING" or "STOPPED" or "PAUSED_PLAYBACK" or "PAUSED_RECORDING" or "RECORDING" or "TRANSITIONING" or
        "NO_MEDIA_PRESENT" or "OK" or "ERROR_OCCURRED" or "UNKNOWN" => value,
        _ => "UNKNOWN"
    };

    /// <summary>Retains source quality with a fixed source name, never device-supplied text.</summary>
    /// <param name="source">Original status.</param>
    /// <param name="name">Known source name for this field.</param>
    private static SourceSnapshot Source(SourceSnapshot source, string name) => new(name, source.LastAttemptSucceeded,
        source.LastAttemptUtc, source.LastSuccessUtc, source.IsStale, Error(source.ErrorCode));

    /// <summary>Allows known diagnostic categories only; future unreviewed text is reduced to an explicit fallback.</summary>
    /// <param name="code">Original bounded category, or null for no error.</param>
    private static string? Error(string? code) => code switch
    {
        null => null,
        "timeout" or "http_failure" or "response_too_large" or "invalid_response" or "probe_failure" or
        "address_reassigned" or "policy_unavailable" or "monitoring_disabled" or "sweep_timeout" or
        "socket_failure" or "response_limit" or "search_cancelled" or "not_supported" or "identity_unconfirmed" or
        "awaiting_initial" or "callback_unconfirmed" or "subscription_failed" or "sequence_gap" or "invalid_notification" or "listener_unavailable" => code,
        _ => "other_error"
    };
}
