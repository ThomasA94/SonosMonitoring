namespace SonosMonitoring.Contracts.Collection;

/// <summary>Transport view of one player, identified by uppercase UDN without uuid:.</summary>
/// <param name="DeviceId">Stable uppercase player UDN without the uuid: prefix.</param>
/// <param name="Address">Current literal IPv4 address.</param>
/// <param name="RoomName">Latest reported room name.</param>
/// <param name="ModelName">Latest reported model name.</param>
/// <param name="FirstSeenUtc">First observation in the current collector process.</param>
/// <param name="LastSeenUtc">Latest validated description observation.</param>
/// <param name="Description">Description source quality.</param>
/// <param name="InterfaceStatus">Interface diagnostic source quality.</param>
/// <param name="Interfaces">Last good raw interface counters.</param>
public sealed record DeviceSnapshotDto(
    string DeviceId,
    string Address,
    string? RoomName,
    string? ModelName,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    SourceSnapshotDto Description,
    SourceSnapshotDto InterfaceStatus,
    IReadOnlyList<InterfaceCountersDto> Interfaces)
{
    /// <summary>Gets the confirmed scheduling decision, or null before evaluation or when policy reads fail.</summary>
    public bool? MonitoringEnabled { get; init; }
    /// <summary>Gets the effective diagnostic polling interval in seconds.</summary>
    public double? EffectiveIntervalSeconds { get; init; }
    /// <summary>Gets the measured description probe duration including bounded retries.</summary>
    public double? ProbeDurationSeconds { get; init; }
    /// <summary>Gets the device-reported software version.</summary>
    public string? SoftwareVersion { get; init; }
    /// <summary>Gets the device-reported hardware version.</summary>
    public string? HardwareVersion { get; init; }
    /// <summary>Gets the device-reported model number.</summary>
    public string? ModelNumber { get; init; }
    /// <summary>Gets current reset-aware interface rates.</summary>
    public IReadOnlyList<InterfaceRateDto> InterfaceRates { get; init; } = [];
    /// <summary>Gets wireless source quality.</summary>
    public SourceSnapshotDto WirelessStatus { get; init; } = new("wireless", false, null, null, true, null);
    /// <summary>Gets Ethernet source quality.</summary>
    public SourceSnapshotDto EthernetStatus { get; init; } = new("ethernet", false, null, null, true, null);
    /// <summary>Gets playback source quality.</summary>
    public SourceSnapshotDto PlaybackStatus { get; init; } = new("playback", false, null, null, true, null);
    /// <summary>Gets paired player volume and mute source quality.</summary>
    public SourceSnapshotDto VolumeStatus { get; init; } = new("volume", false, null, null, true, null);
    /// <summary>Gets the latest reported wireless state.</summary>
    public WirelessObservationDto? Wireless { get; init; }
    /// <summary>Gets the latest reported Ethernet ports.</summary>
    public IReadOnlyList<EthernetPortObservationDto> EthernetPorts { get; init; } = [];
    /// <summary>Gets the latest reported playback state.</summary>
    public PlaybackObservationDto? Playback { get; init; }
    /// <summary>Gets the reported player volume on the device's 0–100 scale.</summary>
    public int? Volume { get; init; }
    /// <summary>Gets the reported player mute state.</summary>
    public bool? Muted { get; init; }

    /// <summary>Gets the pseudonymous metric label for linking protected live data to internal alerts; omitted from reduced exports.</summary>
    public string? MetricLabel { get; init; }

    /// <summary>Gets topology event-path status independently of HTTP source quality.</summary>
    public GenaSnapshotDto Gena { get; init; } = new(false, false, null, null, null, false, null);

    /// <summary>Gets topology quality independently of description and interface counters.</summary>
    public SourceSnapshotDto TopologyStatus { get; init; } = new("topology", false, null, null, true, null);

    /// <summary>Gets the last reported groups; these are observations, not measured network links.</summary>
    public IReadOnlyList<TopologyGroupDto> Groups { get; init; } = [];
}
