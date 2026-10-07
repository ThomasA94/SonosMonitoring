namespace SonosMonitoring.Application.Collection;

/// <summary>Read-only collector view; a missing heartbeat identifies a collector that has never run.</summary>
/// <param name="Enabled">Whether collection is enabled.</param>
/// <param name="HeartbeatUtc">Latest worker heartbeat.</param>
/// <param name="LastSweepUtc">Latest completed collection pass.</param>
/// <param name="Devices">Latest known physical players.</param>
/// <param name="Ranges">Latest configured range results.</param>
/// <param name="CapacityExceeded">Whether valid players were rejected because the live cache is full.</param>
public sealed record CollectorSnapshot(
    bool Enabled,
    DateTimeOffset? HeartbeatUtc,
    DateTimeOffset? LastSweepUtc,
    IReadOnlyList<DeviceSnapshot> Devices,
    IReadOnlyList<DiscoveryRangeSnapshot> Ranges,
    bool CapacityExceeded)
{
    /// <summary>Gets the latest completed sweep duration.</summary>
    public double? SweepDurationSeconds { get; init; }
    /// <summary>Gets description probes attempted during this collector generation, including discovery misses.</summary>
    public ulong ProbeAttemptsTotal { get; init; }
    /// <summary>Gets failed description probes during this collector generation, including discovery misses.</summary>
    public ulong ProbeFailuresTotal { get; init; }
    /// <summary>Gets timed-out description probes during this collector generation.</summary>
    public ulong ProbeTimeoutsTotal { get; init; }
    /// <summary>Gets unique addresses actually probed during the latest full discovery pass.</summary>
    public int DiscoveryAddressesProbed { get; init; }
    /// <summary>Gets successful validated description probes during that deduplicated pass.</summary>
    public int DiscoveryDevicesFound { get; init; }

    /// <summary>Gets the allowed heartbeat age in seconds, including the configured sweep and poll budgets.</summary>
    public long HeartbeatMaxAgeSeconds { get; init; }

    /// <summary>Gets independent SSDP search status without implying callback availability.</summary>
    public SsdpSnapshot Ssdp { get; init; } = new(false, null, null, 0, 0, false, null);

    /// <summary>Gets scheduling diagnostics for topology-derived address hints.</summary>
    public TopologyDiscoverySnapshot TopologyDiscovery { get; init; } = new(null, 0, false);
}
