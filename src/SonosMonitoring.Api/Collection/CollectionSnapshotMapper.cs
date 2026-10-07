using SonosMonitoring.Application.Collection;
using SonosMonitoring.Contracts.Collection;

namespace SonosMonitoring.Api.Collection;

/// <summary>Maps captured application observations to independent HTTP contracts.</summary>
public static class CollectionSnapshotMapper
{
    /// <summary>Maps an internal observation to the independent public transport contract.</summary>
    /// <param name="snapshot">Immutable snapshot of the current cache.</param>
    /// <param name="includeMetricLabels">Whether protected live identities may be linked to metric series.</param>
    public static CollectorSnapshotDto Map(CollectorSnapshot snapshot, bool includeMetricLabels = true) => new(snapshot.Enabled, snapshot.HeartbeatUtc, snapshot.LastSweepUtc,
        snapshot.Devices.Select(device => new DeviceSnapshotDto(device.DeviceId, device.Address, device.RoomName, device.ModelName,
            device.FirstSeenUtc, device.LastSeenUtc, Source(device.Description), Source(device.InterfaceStatus),
            device.Interfaces.Select(item => new InterfaceCountersDto(item.Interface, item.ReceiveBytes, item.TransmitBytes, item.ReceivePackets,
                item.TransmitPackets, item.ReceiveErrors, item.TransmitErrors, item.ReceiveDropped, item.TransmitDropped)).ToArray())
        {
            MonitoringEnabled = device.MonitoringEnabled,
            EffectiveIntervalSeconds = device.EffectiveIntervalSeconds,
            ProbeDurationSeconds = device.ProbeDurationSeconds,
            SoftwareVersion = device.SoftwareVersion,
            HardwareVersion = device.HardwareVersion,
            ModelNumber = device.ModelNumber,
            InterfaceRates = device.InterfaceRates.Select(rate => new InterfaceRateDto(rate.Interface, rate.ReceiveBytesPerSecond,
                rate.TransmitBytesPerSecond, rate.SampledAtUtc, rate.IntervalSeconds)).ToArray(),
            WirelessStatus = Source(device.WirelessStatus),
            EthernetStatus = Source(device.EthernetStatus),
            PlaybackStatus = Source(device.PlaybackStatus),
            VolumeStatus = Source(device.VolumeStatus),
            Wireless = device.Wireless is null ? null : new WirelessObservationDto(device.Wireless.WifiMode, device.Wireless.ConnectionType, device.Wireless.SonosNetDisabled),
            EthernetPorts = device.EthernetPorts.Select(port => new EthernetPortObservationDto(port.Port, port.LinkUp, port.ReportedSpeed)).ToArray(),
            Playback = device.Playback is null ? null : new PlaybackObservationDto(device.Playback.TransportState, device.Playback.TransportStatus),
            Volume = device.Volume,
            Muted = device.Muted,
            MetricLabel = includeMetricLabels ? Infrastructure.Collection.CollectorMetricsFormatter.DeviceLabel(device.DeviceId) : null,
            Gena = new GenaSnapshotDto(device.Gena.Enabled, device.Gena.Subscribed, device.Gena.LastAttemptUtc,
                device.Gena.ExpiresUtc, device.Gena.LastNotificationUtc, device.Gena.InitialNotificationReceived, device.Gena.ErrorCode),
            TopologyStatus = Source(device.TopologyStatus),
            Groups = device.Groups.Select(group => new TopologyGroupDto(group.GroupId, group.CoordinatorDeviceId,
                group.Members.Select(member => new TopologyMemberDto(member.DeviceId, member.RoomName,
                    member.IsInvisible, member.ParentDeviceId)
                { ReportedAddress = member.ReportedAddress }).ToArray())).ToArray()
        }).ToArray(),
        snapshot.Ranges.Select(range => new DiscoveryRangeSnapshotDto(range.Cidr, range.LastAttemptUtc, range.LastCompletedUtc,
            range.LastAttemptSucceeded, range.AddressesProbed, range.DevicesFound, range.ErrorCode)).ToArray(), snapshot.CapacityExceeded)
    {
        SweepDurationSeconds = snapshot.SweepDurationSeconds,
        ProbeAttemptsTotal = snapshot.ProbeAttemptsTotal,
        ProbeFailuresTotal = snapshot.ProbeFailuresTotal,
        ProbeTimeoutsTotal = snapshot.ProbeTimeoutsTotal,
        DiscoveryAddressesProbed = snapshot.DiscoveryAddressesProbed,
        DiscoveryDevicesFound = snapshot.DiscoveryDevicesFound,
        Ssdp = new SsdpSnapshotDto(snapshot.Ssdp.Enabled, snapshot.Ssdp.LastAttemptUtc, snapshot.Ssdp.LastCompletedUtc,
            snapshot.Ssdp.CandidateCount, snapshot.Ssdp.FailedInterfaces, snapshot.Ssdp.LimitReached, snapshot.Ssdp.ErrorCode),
        TopologyDiscovery = new TopologyDiscoverySnapshotDto(snapshot.TopologyDiscovery.LastSelectionUtc,
            snapshot.TopologyDiscovery.CandidateCount, snapshot.TopologyDiscovery.LimitReached)
    };

    /// <summary>Preserves source success, age and error independently of the numerical sample.</summary>
    /// <param name="source">Internal source status.</param>
    private static SourceSnapshotDto Source(SourceSnapshot source) => new(source.Name, source.LastAttemptSucceeded, source.LastAttemptUtc,
        source.LastSuccessUtc, source.IsStale, source.ErrorCode);
}
