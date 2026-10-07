namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Explicit target boundaries and resource limits for read-only player collection.</summary>
public sealed class CollectorOptions
{
    /// <summary>Gets or sets the optional topology event receiver configuration.</summary>
    public Gena.GenaOptions Gena { get; set; } = new();

    /// <summary>Enables background network collection.</summary>
    public bool Enabled { get; set; }

    /// <summary>Allowed IPv4 networks; an empty list prohibits every request.</summary>
    public List<string> AllowedCidrs { get; set; } = [];

    /// <summary>Optional literal IPv4 addresses to check before a full range scan.</summary>
    public List<string> SeedAddresses { get; set; } = [];

    /// <summary>Permits loopback test devices only when the host environment is Testing.</summary>
    public bool AllowLoopback { get; set; }

    /// <summary>Opts into SSDP searches on explicitly selected local IPv4 interfaces.</summary>
    public bool SsdpEnabled { get; set; }

    /// <summary>Local IPv4 interface addresses used as SSDP sources; never inferred from target CIDRs.</summary>
    public List<string> SsdpInterfaceAddresses { get; set; } = [];

    /// <summary>Shared SSDP receive window before a full unicast sweep.</summary>
    public TimeSpan SsdpSearchWindow { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Maximum received UDP datagrams per selected interface, including rejected packets.</summary>
    public int SsdpMaxDatagrams { get; set; } = 64;

    /// <summary>Interval between probes of known players and seeds.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Interval between full range scans.</summary>
    public TimeSpan DiscoveryInterval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Time after which retained observations are marked stale.</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>Maximum total number of distinct addresses per full scan.</summary>
    public int MaxScanAddresses { get; set; } = 1024;

    /// <summary>Maximum number of distinct player identities retained in the live cache.</summary>
    public int MaxKnownDevices { get; set; } = 256;

    /// <summary>Maximum additional topology candidates per pass, before a periodic full sweep adds its remaining hosts.</summary>
    public int MaxTopologyCandidates { get; set; } = 32;

    /// <summary>Maximum number of simultaneous device requests.</summary>
    public int MaxParallelism { get; set; } = 16;

    /// <summary>Time budget for one HTTP request.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Time budget for a complete collection pass.</summary>
    public TimeSpan SweepTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Maximum accepted response body size.</summary>
    public int MaxResponseBytes { get; set; } = 256 * 1024;
}
