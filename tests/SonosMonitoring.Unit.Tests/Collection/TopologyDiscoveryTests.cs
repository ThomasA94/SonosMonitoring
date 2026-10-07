using SonosMonitoring.Application.Collection;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Verifies topology-derived address selection, freshness, bounds, and retry scheduling.</summary>
public sealed class TopologyDiscoveryTests
{
    /// <summary>Gets the fixed reference time shared by each test scenario.</summary>
    private DateTimeOffset Now => new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Uses only topology advertised by reporters with fresh successful description and topology sources.</summary>
    [Fact]
    public void SelectsOnlyHintsFromFreshSuccessfulReporters()
    {
        TopologyDiscovery discovery = this.CreateDiscovery();
        CollectorSnapshot snapshot = this.Snapshot(
            this.Device("RINCON_FRESH", true, false, true, false, this.Members("192.168.6.2")),
            this.Device("RINCON_DESCRIPTION_FAILED", false, false, true, false, this.Members("192.168.6.3")),
            this.Device("RINCON_DESCRIPTION_STALE", true, true, true, false, this.Members("192.168.6.4")),
            this.Device("RINCON_TOPOLOGY_FAILED", true, false, false, false, this.Members("192.168.6.5")),
            this.Device("RINCON_TOPOLOGY_STALE", true, false, true, true, this.Members("192.168.6.6")));

        TopologyCandidateSelection selection = discovery.Select(snapshot, this.Scheduled(), this.Now);

        Assert.Equal(["192.168.6.2"], selection.Addresses);
    }

    /// <summary>Filters hints that are invalid, outside the configured ranges, or the CIDR broadcast address.</summary>
    [Fact]
    public void SelectsOnlyBoundedUsableUnicastHosts()
    {
        TopologyDiscovery discovery = this.CreateDiscovery();
        CollectorSnapshot snapshot = this.Snapshot(this.Device("RINCON_REPORTER", true, false, true, false,
            this.Members("192.168.6.2", "192.168.7.2", "192.168.6.7", "192.168.6.01", "192.168.6.3")));

        TopologyCandidateSelection selection = discovery.Select(snapshot, this.Scheduled(), this.Now);

        Assert.Equal(["192.168.6.2", "192.168.6.3"], selection.Addresses);
    }

    /// <summary>Deduplicates member and satellite addresses across groups and independent reporters.</summary>
    [Fact]
    public void DeduplicatesRepeatedHintsAcrossMembersAndReporters()
    {
        TopologyDiscovery discovery = this.CreateDiscovery();
        CollectorSnapshot snapshot = this.Snapshot(
            this.Device("RINCON_FIRST", true, false, true, false,
                this.Members("192.168.6.2", "192.168.6.3"), this.Members("192.168.6.3")),
            this.Device("RINCON_SECOND", true, false, true, false, this.Members("192.168.6.2", "192.168.6.4")));

        TopologyCandidateSelection selection = discovery.Select(snapshot, this.Scheduled(), this.Now);

        Assert.Equal(["192.168.6.2", "192.168.6.3", "192.168.6.4"], selection.Addresses);
    }

    /// <summary>Caps a selection, reports remaining eligible work, and rotates fairly after the last address.</summary>
    [Fact]
    public void CapsAndRotatesCandidateSelection()
    {
        TopologyDiscovery discovery = this.CreateDiscovery(maximumCandidates: 2);
        CollectorSnapshot snapshot = this.Snapshot(this.Device("RINCON_REPORTER", true, false, true, false,
            this.Members("192.168.6.2", "192.168.6.3", "192.168.6.4", "192.168.6.5")));

        TopologyCandidateSelection first = discovery.Select(snapshot, this.Scheduled(), this.Now);
        TopologyCandidateSelection second = discovery.Select(snapshot, this.Scheduled(), this.Now.AddSeconds(10));

        Assert.Equal(["192.168.6.2", "192.168.6.3"], first.Addresses);
        Assert.True(first.LimitReached);
        Assert.Equal(["192.168.6.4", "192.168.6.5"], second.Addresses);
        Assert.True(second.LimitReached);
    }

    /// <summary>Does not select addresses already scheduled through seeds or known-player polling.</summary>
    [Fact]
    public void ExcludesAddressesAlreadyScheduledInThePass()
    {
        TopologyDiscovery discovery = this.CreateDiscovery();
        CollectorSnapshot snapshot = this.Snapshot(this.Device("RINCON_REPORTER", true, false, true, false,
            this.Members("192.168.6.2", "192.168.6.3")));

        TopologyCandidateSelection selection = discovery.Select(snapshot,
            new HashSet<string>(["192.168.6.2"], StringComparer.Ordinal), this.Now);

        Assert.Equal(["192.168.6.3"], selection.Addresses);
    }

    /// <summary>Applies the scan-address budget even when multiple configured ranges each yield candidates.</summary>
    [Fact]
    public void CapsCandidatesAtTheScanAddressBudget()
    {
        CollectorOptions options = new()
        {
            MaxScanAddresses = 2,
            MaxTopologyCandidates = 8,
            PollInterval = TimeSpan.FromSeconds(10)
        };
        TopologyDiscovery discovery = new(options,
            new Ipv4TargetPolicy(["192.168.6.0/30", "192.168.7.0/30"]));
        CollectorSnapshot snapshot = this.Snapshot(this.Device("RINCON_REPORTER", true, false, true, false,
            this.Members("192.168.6.1", "192.168.6.2", "192.168.7.1", "192.168.7.2")));

        TopologyCandidateSelection selection = discovery.Select(snapshot, this.Scheduled(), this.Now);

        Assert.Equal(2, selection.Addresses.Count);
        Assert.True(selection.LimitReached);
    }

    /// <summary>Reserves selected candidates for the poll interval and backs off failed probes with bounded jitter.</summary>
    [Fact]
    public void ReservesCandidatesAndBacksOffFailures()
    {
        TopologyDiscovery discovery = this.CreateDiscovery();
        CollectorSnapshot snapshot = this.Snapshot(this.Device("RINCON_REPORTER", true, false, true, false,
            this.Members("192.168.6.2")));

        Assert.Single(discovery.Select(snapshot, this.Scheduled(), this.Now).Addresses);
        Assert.Empty(discovery.Select(snapshot, this.Scheduled(), this.Now.AddSeconds(9)).Addresses);
        Assert.Single(discovery.Select(snapshot, this.Scheduled(), this.Now.AddSeconds(10)).Addresses);

        discovery.RecordResult("192.168.6.2", false, this.Now.AddSeconds(10));

        Assert.Empty(discovery.Select(snapshot, this.Scheduled(), this.Now.AddSeconds(29.9)).Addresses);
        Assert.Single(discovery.Select(snapshot, this.Scheduled(), this.Now.AddSeconds(31)).Addresses);
    }

    /// <summary>Clears the retry delay after a candidate resolves to a valid player description.</summary>
    [Fact]
    public void SuccessfulProbeReleasesFailureBackoff()
    {
        TopologyDiscovery discovery = this.CreateDiscovery();
        CollectorSnapshot snapshot = this.Snapshot(this.Device("RINCON_REPORTER", true, false, true, false,
            this.Members("192.168.6.2")));
        discovery.Select(snapshot, this.Scheduled(), this.Now);
        discovery.RecordResult("192.168.6.2", false, this.Now);

        discovery.Select(snapshot, this.Scheduled(), this.Now.AddSeconds(20));
        discovery.RecordResult("192.168.6.2", true, this.Now.AddSeconds(20));

        Assert.Single(discovery.Select(snapshot, this.Scheduled(), this.Now.AddSeconds(20)).Addresses);
    }

    /// <summary>Creates a selector constrained to a small test range and bounded candidate limit.</summary>
    /// <param name="maximumCandidates">Optional candidate cap.</param>
    /// <returns>A topology selector over 192.168.6.0/29.</returns>
    private TopologyDiscovery CreateDiscovery(int maximumCandidates = 8)
    {
        CollectorOptions options = new()
        {
            MaxScanAddresses = 8,
            MaxTopologyCandidates = maximumCandidates,
            PollInterval = TimeSpan.FromSeconds(10),
            StaleAfter = TimeSpan.FromMinutes(1)
        };

        return new TopologyDiscovery(options, new Ipv4TargetPolicy(["192.168.6.0/29"]));
    }

    /// <summary>Creates a device whose source status can be varied independently.</summary>
    /// <param name="deviceId">Reporter identity.</param>
    /// <param name="descriptionSucceeded">Whether the latest description attempt succeeded.</param>
    /// <param name="descriptionStale">Whether the last good description is stale.</param>
    /// <param name="topologySucceeded">Whether the latest topology attempt succeeded.</param>
    /// <param name="topologyStale">Whether the last good topology is stale.</param>
    /// <param name="groups">Reported groups and hints.</param>
    /// <returns>A reporter snapshot with the requested source quality.</returns>
    private DeviceSnapshot Device(string deviceId, bool descriptionSucceeded, bool descriptionStale,
        bool topologySucceeded, bool topologyStale, params TopologyMember[][] groups)
    {
        SourceSnapshot description = new("description", descriptionSucceeded, this.Now, this.Now, descriptionStale,
            descriptionSucceeded ? null : "http_failure");
        SourceSnapshot interfaces = new("interfaces", true, this.Now, this.Now, false, null);
        DeviceSnapshot device = new(deviceId, "192.168.6.1", null, null, this.Now, this.Now, description, interfaces, [])
        {
            TopologyStatus = new SourceSnapshot("topology", topologySucceeded, this.Now, this.Now, topologyStale,
                topologySucceeded ? null : "http_failure"),
            Groups = groups.Select((members, index) => new TopologyGroup($"group-{index}", deviceId, members)).ToArray()
        };

        return device;
    }

    /// <summary>Creates reported members with the supplied advertised addresses.</summary>
    /// <param name="addresses">Candidate address strings.</param>
    /// <returns>Members containing each reported address.</returns>
    private TopologyMember[] Members(params string[] addresses)
    {
        return addresses.Select((address, index) => new TopologyMember($"RINCON_MEMBER_{index}", null, null, null)
        {
            ReportedAddress = address
        }).ToArray();
    }

    /// <summary>Creates a collector snapshot for the supplied devices.</summary>
    /// <param name="devices">Current device snapshots.</param>
    /// <returns>A snapshot containing those devices.</returns>
    private CollectorSnapshot Snapshot(params DeviceSnapshot[] devices)
    {
        return new CollectorSnapshot(true, this.Now, this.Now, devices, [], false);
    }

    /// <summary>Creates an empty set of targets already scheduled in the collection pass.</summary>
    /// <returns>An ordinal address set.</returns>
    private HashSet<string> Scheduled()
    {
        return new HashSet<string>(StringComparer.Ordinal);
    }
}
