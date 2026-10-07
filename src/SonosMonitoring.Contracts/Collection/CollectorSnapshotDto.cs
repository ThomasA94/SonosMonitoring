namespace SonosMonitoring.Contracts.Collection;

/// <summary>Transport view of collector liveness, discovered players, and search ranges.</summary>
/// <param name="Enabled">Whether collection is enabled.</param>
/// <param name="HeartbeatUtc">Latest worker heartbeat, or null before first execution.</param>
/// <param name="LastSweepUtc">Latest completed collection pass.</param>
/// <param name="Devices">Known physical players.</param>
/// <param name="Ranges">Latest result for each configured search range.</param>
/// <param name="CapacityExceeded">Whether valid players were rejected because the live cache is full.</param>
public sealed record CollectorSnapshotDto(
    bool Enabled,
    DateTimeOffset? HeartbeatUtc,
    DateTimeOffset? LastSweepUtc,
    IReadOnlyList<DeviceSnapshotDto> Devices,
    IReadOnlyList<DiscoveryRangeSnapshotDto> Ranges,
    bool CapacityExceeded)
{
    /// <summary>Gets the latest completed sweep duration.</summary>
    public double? SweepDurationSeconds { get; init; }
    /// <summary>Gets description probes in this collector generation, including discovery misses.</summary>
    public ulong ProbeAttemptsTotal { get; init; }
    /// <summary>Gets failed description probes, including expected discovery misses.</summary>
    public ulong ProbeFailuresTotal { get; init; }
    /// <summary>Gets timed-out description probes in this collector generation.</summary>
    public ulong ProbeTimeoutsTotal { get; init; }
    /// <summary>Gets deduplicated addresses actually probed during the latest full discovery pass.</summary>
    public int DiscoveryAddressesProbed { get; init; }
    /// <summary>Gets successful validated description probes during that deduplicated pass.</summary>
    public int DiscoveryDevicesFound { get; init; }

    /// <summary>Gets discovery progress independently of HTTP reachability and future event callbacks.</summary>
    public SsdpSnapshotDto Ssdp { get; init; } = new(false, null, null, 0, 0, false, null);

    /// <summary>Gets topology hint selection independently of validated device observations.</summary>
    public TopologyDiscoverySnapshotDto TopologyDiscovery { get; init; } = new(null, 0, false);
}
