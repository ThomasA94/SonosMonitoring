using SonosMonitoring.Application.Collection.Diagnostics;

namespace SonosMonitoring.Application.Collection;

/// <summary>Latest observations for one physical player; DeviceId is uppercase UDN without the uuid: prefix.</summary>
/// <param name="DeviceId">Stable uppercase player UDN without uuid:.</param>
/// <param name="Address">Most recently validated literal IPv4 address.</param>
/// <param name="RoomName">Last reported room name.</param>
/// <param name="ModelName">Last reported model name.</param>
/// <param name="FirstSeenUtc">First observation in this collector process.</param>
/// <param name="LastSeenUtc">Latest validated description observation.</param>
/// <param name="Description">Description source quality.</param>
/// <param name="InterfaceStatus">Interface diagnostic source quality.</param>
/// <param name="Interfaces">Last good raw counters.</param>
public sealed record DeviceSnapshot(
    string DeviceId,
    string Address,
    string? RoomName,
    string? ModelName,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    SourceSnapshot Description,
    SourceSnapshot InterfaceStatus,
    IReadOnlyList<InterfaceCounters> Interfaces)
{
    /// <summary>Gets the last confirmed scheduling decision; null means unavailable or not yet evaluated.</summary>
    public bool? MonitoringEnabled { get; init; }
    /// <summary>Gets the effective diagnostic interval including the collector's polling floor.</summary>
    public double? EffectiveIntervalSeconds { get; init; }
    /// <summary>Gets the reported software version.</summary>
    public string? SoftwareVersion { get; init; }
    /// <summary>Gets the reported hardware version.</summary>
    public string? HardwareVersion { get; init; }
    /// <summary>Gets the reported model number.</summary>
    public string? ModelNumber { get; init; }
    /// <summary>Gets measured per-interface rates from consecutive successful samples.</summary>
    public IReadOnlyList<InterfaceRate> InterfaceRates { get; init; } = [];
    /// <summary>Gets wireless source evidence.</summary>
    public SourceSnapshot WirelessStatus { get; init; } = new("wireless", false, null, null, true, null);
    /// <summary>Gets Ethernet source evidence.</summary>
    public SourceSnapshot EthernetStatus { get; init; } = new("ethernet", false, null, null, true, null);
    /// <summary>Gets playback source evidence.</summary>
    public SourceSnapshot PlaybackStatus { get; init; } = new("playback", false, null, null, true, null);
    /// <summary>Gets volume and mute source evidence, successful only when both reads succeeded.</summary>
    public SourceSnapshot VolumeStatus { get; init; } = new("volume", false, null, null, true, null);
    /// <summary>Gets the latest reported wireless state.</summary>
    public WirelessObservation? Wireless { get; init; }
    /// <summary>Gets the latest reported Ethernet ports.</summary>
    public IReadOnlyList<EthernetPortObservation> EthernetPorts { get; init; } = [];
    /// <summary>Gets the latest reported playback state.</summary>
    public PlaybackObservation? Playback { get; init; }
    /// <summary>Gets the last reported player volume, independently of group volume.</summary>
    public int? Volume { get; init; }
    /// <summary>Gets the last reported player mute state.</summary>
    public bool? Muted { get; init; }
    /// <summary>Gets the measured duration of the latest description request including retries.</summary>
    public double? ProbeDurationSeconds { get; init; }

    /// <summary>Gets whether the validated description advertises the supported topology event endpoint.</summary>
    public bool SupportsTopologyEvents { get; init; }

    /// <summary>Gets callback-path evidence separately from polled topology data.</summary>
    public GenaSnapshot Gena { get; init; } = new(false, false, null, null, null, false, null);

    /// <summary>Gets the independent topology source status reported by this player.</summary>
    public SourceSnapshot TopologyStatus { get; init; } = new("topology", false, null, null, true, null);

    /// <summary>Gets the last complete group observation from this player, without merging other reporters.</summary>
    public IReadOnlyList<TopologyGroup> Groups { get; init; } = [];
}
