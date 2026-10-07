using SonosMonitoring.Application.Collection;
using SonosMonitoring.Application.Collection.Diagnostics;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Thread-safe bounded live cache of player observations and scan progress.</summary>
public sealed class CollectorState : ICollectorSnapshotReader
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DeviceSnapshot> _devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DiscoveryRangeSnapshot> _ranges = new(StringComparer.Ordinal);
    private readonly CollectorOptions _options;
    private DateTimeOffset? _heartbeatUtc;
    private DateTimeOffset? _lastSweepUtc;
    private bool _capacityExceeded;
    private double? _sweepDurationSeconds;
    private ulong _probeAttemptsTotal;
    private ulong _probeFailuresTotal;
    private ulong _probeTimeoutsTotal;
    private int _discoveryAddressesProbed;
    private int _discoveryDevicesFound;
    private SsdpSnapshot _ssdp;
    private TopologyDiscoverySnapshot _topologyDiscovery = new(null, 0, false);

    /// <summary>Creates an empty collector state.</summary>
    /// <param name="options">Freshness and enablement settings.</param>
    public CollectorState(CollectorOptions options)
    {
        this._options = options;
        this._ssdp = new SsdpSnapshot(options.Enabled && options.SsdpEnabled, null, null, 0, 0, false, null);
    }

    /// <summary>Replaces the latest optional discovery result independently of device observations.</summary>
    /// <param name="snapshot">Completed or interrupted SSDP attempt.</param>
    public void SsdpResult(SsdpSnapshot snapshot)
    {
        lock (this._gate)
        {
            this._ssdp = snapshot;
        }
    }

    /// <summary>Records bounded address-hint scheduling without implying HTTP success.</summary>
    /// <param name="selection">Candidate count and cap status.</param>
    /// <param name="now">Selection time.</param>
    public void TopologyCandidatesSelected(TopologyCandidateSelection selection, DateTimeOffset now)
    {
        lock (this._gate)
        {
            this._topologyDiscovery = new TopologyDiscoverySnapshot(now, selection.Addresses.Count, selection.LimitReached);
        }
    }

    /// <summary>Records that the hosted worker is alive.</summary>
    /// <param name="now">Current UTC time.</param>
    public void Heartbeat(DateTimeOffset now)
    {
        lock (this._gate)
        {
            this._heartbeatUtc = now;
        }
    }

    /// <summary>Records unique actual results for the latest full discovery pass, including an interrupted pass.</summary>
    /// <param name="addressesProbed">Deduplicated completed address probes.</param>
    /// <param name="devicesFound">Successful validated description probes.</param>
    public void DiscoveryResult(int addressesProbed, int devicesFound)
    {
        lock (this._gate)
        {
            this._discoveryAddressesProbed = addressesProbed;
            this._discoveryDevicesFound = devicesFound;
        }
    }

    /// <summary>Records completion of a collection pass.</summary>
    /// <param name="now">Completion UTC time.</param>
    /// <param name="durationSeconds">Actual completed sweep duration, if measured.</param>
    public void SweepCompleted(DateTimeOffset now, double? durationSeconds = null)
    {
        lock (this._gate)
        {
            this._lastSweepUtc = now;
            this._sweepDurationSeconds = durationSeconds;
        }
    }

    /// <summary>Records a validated player description keyed by stable UDN.</summary>
    /// <param name="description">Validated description.</param>
    /// <param name="address">Current literal IPv4 address.</param>
    /// <param name="now">Observation UTC time.</param>
    /// <returns>Whether the player fit within the bounded live cache.</returns>
    public bool DescriptionSucceeded(PlayerDescription description, string address, DateTimeOffset now)
    {
        lock (this._gate)
        {
            foreach (DeviceSnapshot previous in this._devices.Values.Where(device =>
                device.Address == address && device.DeviceId != description.DeviceId).ToArray())
            {
                this._devices[previous.DeviceId] = previous with
                {
                    Description = previous.Description with
                    {
                        LastAttemptSucceeded = false,
                        LastAttemptUtc = now,
                        ErrorCode = "address_reassigned"
                    }
                };
            }

            if (this._devices.TryGetValue(description.DeviceId, out DeviceSnapshot? old) == false &&
                this._devices.Count >= this._options.MaxKnownDevices)
            {
                this._capacityExceeded = true;
                return false;
            }

            old ??= new DeviceSnapshot(description.DeviceId, address, null, null, now, now,
                new SourceSnapshot("description", false, null, null, true, null),
                new SourceSnapshot("ifconfig", false, null, null, true, null), [])
            {
                Gena = new GenaSnapshot(this._options.Gena.Enabled, false, null, null, null, false,
                    this._options.Gena.Enabled ? "awaiting_initial" : null)
            };
            SourceSnapshot source = new("description", true, now, now, false, null);
            this._devices[description.DeviceId] = old with
            {
                Address = address,
                RoomName = description.RoomName,
                ModelName = description.ModelName,
                SupportsTopologyEvents = description.SupportsTopologyEvents,
                SoftwareVersion = description.SoftwareVersion,
                HardwareVersion = description.HardwareVersion,
                ModelNumber = description.ModelNumber,
                LastSeenUtc = now,
                Description = source
            };
            return true;
        }
    }

    /// <summary>Publishes bounded event receiver status without changing polled source observations.</summary>
    /// <param name="id">Already admitted player.</param>
    /// <param name="snapshot">Current lease and callback evidence.</param>
    public void GenaResult(string id, GenaSnapshot snapshot)
    {
        lock (this._gate)
        {
            if (this._devices.TryGetValue(id, out var device))
            {
                this._devices[id] = device with { Gena = snapshot };
            }
        }
    }

    /// <summary>Records a failed description request without deleting prior identity.</summary>
    /// <param name="address">Attempted address.</param>
    /// <param name="now">Attempt UTC time.</param>
    /// <param name="errorCode">Bounded category, not a raw exception.</param>
    public void DescriptionFailed(string address, DateTimeOffset now, string errorCode)
    {
        lock (this._gate)
        {
            foreach (DeviceSnapshot device in this._devices.Values.Where(device => device.Address == address).ToArray())
            {
                this._devices[device.DeviceId] = device with
                {
                    Description = device.Description with { LastAttemptSucceeded = false, LastAttemptUtc = now, ErrorCode = errorCode }
                };
            }
        }
    }

    /// <summary>Updates raw counters after a valid diagnostic response.</summary>
    /// <param name="id">Stable player UDN.</param>
    /// <param name="counters">Parsed counters.</param>
    /// <param name="now">Observation UTC time.</param>
    public void InterfaceSucceeded(string id, IReadOnlyList<InterfaceCounters> counters, DateTimeOffset now)
    {
        lock (this._gate)
        {
            if (this._devices.TryGetValue(id, out DeviceSnapshot? old))
            {
                this._devices[id] = old with
                {
                    InterfaceStatus = new SourceSnapshot("ifconfig", true, now, now, false, null),
                    Interfaces = counters.ToArray(),
                    InterfaceRates = InterfaceRateCalculator.Calculate(old.Interfaces, old.InterfaceStatus.LastSuccessUtc, counters,
                        now, this.DiagnosticFreshnessSeconds(old))
                };
            }
        }
    }

    /// <summary>Marks diagnostics unavailable while retaining the last good counters.</summary>
    /// <param name="id">Stable player UDN.</param>
    /// <param name="now">Attempt UTC time.</param>
    /// <param name="errorCode">Bounded failure category.</param>
    public void InterfaceFailed(string id, DateTimeOffset now, string errorCode)
    {
        lock (this._gate)
        {
            if (this._devices.TryGetValue(id, out DeviceSnapshot? old))
            {
                this._devices[id] = old with
                {
                    InterfaceStatus = old.InterfaceStatus with { LastAttemptSucceeded = false, LastAttemptUtc = now, ErrorCode = errorCode }
                };
            }
        }
    }

    /// <summary>Records description transport budgets including discovery addresses without validated identities.</summary>
    /// <param name="address">Approved literal target.</param>
    /// <param name="durationSeconds">Measured request duration including bounded retries.</param>
    /// <param name="errorCode">Failure category, or null after success.</param>
    public void DescriptionProbeFinished(string address, double durationSeconds, string? errorCode)
    {
        lock (this._gate)
        {
            this._probeAttemptsTotal++;
            if (errorCode is not null)
            {
                this._probeFailuresTotal++;
            }
            if (errorCode == "timeout")
            {
                this._probeTimeoutsTotal++;
            }
            foreach (DeviceSnapshot device in this._devices.Values.Where(device => device.Address == address).ToArray())
            {
                this._devices[device.DeviceId] = device with { ProbeDurationSeconds = durationSeconds };
            }
        }
    }

    /// <summary>Records the confirmed scheduling decision without claiming a diagnostic request succeeded.</summary>
    /// <param name="id">Admitted player identity.</param>
    /// <param name="enabled">Confirmed permission, or null for unavailable policy.</param>
    /// <param name="intervalSeconds">Effective interval including the global polling floor.</param>
    /// <param name="now">Decision time.</param>
    public void DiagnosticDecision(string id, bool? enabled, double? intervalSeconds, DateTimeOffset now)
    {
        lock (this._gate)
        {
            if (this._devices.TryGetValue(id, out var device))
            {
                this._devices[id] = device with { MonitoringEnabled = enabled, EffectiveIntervalSeconds = intervalSeconds };
                if (enabled != true)
                {
                    string code = enabled == false ? "monitoring_disabled" : "policy_unavailable";
                    foreach (string source in new[] { "ifconfig", "wireless", "ethernet", "playback", "volume" })
                    {
                        this.DiagnosticFailed(id, source, now, code);
                    }
                }
            }
        }
    }

    /// <summary>Marks one independent diagnostic source unsuccessful while retaining its last good observation.</summary>
    /// <param name="id">Admitted identity.</param>
    /// <param name="source">Application-owned diagnostic source name.</param>
    /// <param name="now">Attempt time.</param>
    /// <param name="errorCode">Bounded failure or intentional unavailability code.</param>
    public void DiagnosticFailed(string id, string source, DateTimeOffset now, string errorCode)
    {
        lock (this._gate)
        {
            if (this._devices.TryGetValue(id, out var device))
            {
                SourceSnapshot previous = source switch
                {
                    "ifconfig" => device.InterfaceStatus,
                    "wireless" => device.WirelessStatus,
                    "ethernet" => device.EthernetStatus,
                    "playback" => device.PlaybackStatus,
                    "volume" => device.VolumeStatus,
                    _ => throw new ArgumentException("Unknown diagnostic source.", nameof(source))
                };
                SourceSnapshot failed = previous with { LastAttemptSucceeded = false, LastAttemptUtc = now, ErrorCode = errorCode };
                this._devices[id] = source switch
                {
                    "ifconfig" => device with { InterfaceStatus = failed },
                    "wireless" => device with { WirelessStatus = failed },
                    "ethernet" => device with { EthernetStatus = failed },
                    "playback" => device with { PlaybackStatus = failed },
                    "volume" => device with { VolumeStatus = failed },
                    _ => device
                };
            }
        }
    }

    /// <summary>Publishes a valid wireless observation and its source time.</summary>
    /// <param name="id">Admitted identity.</param>
    /// <param name="observation">Parsed reported mode.</param>
    /// <param name="now">Successful observation time.</param>
    public void WirelessSucceeded(string id, WirelessObservation observation, DateTimeOffset now)
    {
        lock (this._gate)
        {
            if (this._devices.TryGetValue(id, out var device))
            {
                this._devices[id] = device with { Wireless = observation, WirelessStatus = Success("wireless", now) };
            }
        }
    }

    /// <summary>Publishes a complete Ethernet port observation and its source time.</summary>
    /// <param name="id">Admitted identity.</param>
    /// <param name="ports">Parsed bounded port list.</param>
    /// <param name="now">Successful observation time.</param>
    public void EthernetSucceeded(string id, IReadOnlyList<EthernetPortObservation> ports, DateTimeOffset now)
    {
        lock (this._gate)
        {
            if (this._devices.TryGetValue(id, out var device))
            {
                this._devices[id] = device with { EthernetPorts = ports.ToArray(), EthernetStatus = Success("ethernet", now) };
            }
        }
    }

    /// <summary>Publishes read-only transport state without implying audio quality.</summary>
    /// <param name="id">Admitted identity.</param>
    /// <param name="observation">Validated transport response.</param>
    /// <param name="now">Successful observation time.</param>
    public void PlaybackSucceeded(string id, PlaybackObservation observation, DateTimeOffset now)
    {
        lock (this._gate)
        {
            if (this._devices.TryGetValue(id, out var device))
            {
                this._devices[id] = device with { Playback = observation, PlaybackStatus = Success("playback", now) };
            }
        }
    }

    /// <summary>Publishes paired player volume and mute only after both independent reads succeeded.</summary>
    /// <param name="id">Admitted identity.</param>
    /// <param name="volume">Validated player volume.</param>
    /// <param name="muted">Validated player mute.</param>
    /// <param name="now">Completion time of both reads.</param>
    public void VolumeSucceeded(string id, int volume, bool muted, DateTimeOffset now)
    {
        lock (this._gate)
        {
            if (this._devices.TryGetValue(id, out var device))
            {
                this._devices[id] = device with { Volume = volume, Muted = muted, VolumeStatus = Success("volume", now) };
            }
        }
    }

    /// <summary>Creates fresh successful source evidence.</summary>
    /// <param name="name">Application-owned source.</param>
    /// <param name="now">Observation time.</param>
    private static SourceSnapshot Success(string name, DateTimeOffset now) => new(name, true, now, now, false, null);

    /// <summary>Allows three actual diagnostic periods before a previously successful sample expires.</summary>
    /// <param name="device">Last confirmed effective interval.</param>
    private double DiagnosticFreshnessSeconds(DeviceSnapshot device) =>
        Math.Max(this._options.StaleAfter.TotalSeconds, 3 * (device.EffectiveIntervalSeconds ?? this._options.PollInterval.TotalSeconds));

    /// <summary>Atomically replaces the complete topology reported by one admitted player.</summary>
    /// <param name="id">Reporting player UDN.</param>
    /// <param name="groups">Validated groups from this response.</param>
    /// <param name="now">Successful observation time.</param>
    public void TopologySucceeded(string id, IReadOnlyList<TopologyGroup> groups, DateTimeOffset now)
    {
        lock (this._gate)
        {
            if (this._devices.TryGetValue(id, out DeviceSnapshot? old))
            {
                this._devices[id] = old with
                {
                    TopologyStatus = new SourceSnapshot("topology", true, now, now, false, null),
                    Groups = CopyGroups(groups)
                };
            }
        }
    }

    /// <summary>Retains the last complete topology while exposing its latest failed attempt.</summary>
    /// <param name="id">Reporting player UDN.</param>
    /// <param name="now">Failed attempt time.</param>
    /// <param name="errorCode">Bounded category without raw payloads.</param>
    public void TopologyFailed(string id, DateTimeOffset now, string errorCode)
    {
        lock (this._gate)
        {
            if (this._devices.TryGetValue(id, out DeviceSnapshot? old))
            {
                this._devices[id] = old with
                {
                    TopologyStatus = old.TopologyStatus with { LastAttemptSucceeded = false, LastAttemptUtc = now, ErrorCode = errorCode }
                };
            }
        }
    }

    /// <summary>Records a completed range pass and its partial or complete result.</summary>
    /// <param name="range">Canonical CIDR.</param>
    /// <param name="attempt">Attempt UTC time.</param>
    /// <param name="completed">Completion time, or null on interruption.</param>
    /// <param name="probed">Attempted addresses.</param>
    /// <param name="found">Validated players.</param>
    /// <param name="errorCode">Bounded error category, if any.</param>
    public void RangeResult(string range, DateTimeOffset attempt, DateTimeOffset? completed, int probed, int found, string? errorCode)
    {
        lock (this._gate)
        {
            this._ranges[range] = new DiscoveryRangeSnapshot(range, attempt, completed,
                completed.HasValue && errorCode is null, probed, found, errorCode);
        }
    }

    /// <summary>Copies the live state and recalculates staleness at the time of reading.</summary>
    /// <returns>An immutable view independent of subsequent updates.</returns>
    public CollectorSnapshot GetSnapshot()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (this._gate)
        {
            DeviceSnapshot[] devices = this._devices.Values.Select(device => device with
            {
                Description = this.WithStaleness(device.Description, now),
                InterfaceStatus = this.WithStaleness(device.InterfaceStatus, now, this.DiagnosticFreshnessSeconds(device)),
                TopologyStatus = this.WithStaleness(device.TopologyStatus, now),
                WirelessStatus = this.WithStaleness(device.WirelessStatus, now, this.DiagnosticFreshnessSeconds(device)),
                EthernetStatus = this.WithStaleness(device.EthernetStatus, now, this.DiagnosticFreshnessSeconds(device)),
                PlaybackStatus = this.WithStaleness(device.PlaybackStatus, now, this.DiagnosticFreshnessSeconds(device)),
                VolumeStatus = this.WithStaleness(device.VolumeStatus, now, this.DiagnosticFreshnessSeconds(device)),
                Groups = CopyGroups(device.Groups),
                Interfaces = device.Interfaces.ToArray(),
                EthernetPorts = device.EthernetPorts.ToArray(),
                InterfaceRates = device.InterfaceStatus.LastAttemptSucceeded && device.InterfaceStatus.LastSuccessUtc is DateTimeOffset lastSuccess &&
                    (now - lastSuccess).TotalSeconds <= this.DiagnosticFreshnessSeconds(device) &&
                    device.InterfaceStatus.ErrorCode is not ("monitoring_disabled" or "policy_unavailable") ? device.InterfaceRates.ToArray() : []
            }).OrderBy(device => device.DeviceId, StringComparer.Ordinal).ToArray();
            return new CollectorSnapshot(this._options.Enabled, this._heartbeatUtc, this._lastSweepUtc,
                devices, this._ranges.Values.OrderBy(range => range.Cidr, StringComparer.Ordinal).ToArray(),
                this._capacityExceeded)
            {
                Ssdp = this._ssdp,
                SweepDurationSeconds = this._sweepDurationSeconds,
                ProbeAttemptsTotal = this._probeAttemptsTotal,
                ProbeFailuresTotal = this._probeFailuresTotal,
                ProbeTimeoutsTotal = this._probeTimeoutsTotal,
                DiscoveryAddressesProbed = this._discoveryAddressesProbed,
                DiscoveryDevicesFound = this._discoveryDevicesFound,
                TopologyDiscovery = this._topologyDiscovery,
                // A heartbeat occurs before a bounded sweep, then the next timer tick may still be pending.
                HeartbeatMaxAgeSeconds = (long)Math.Ceiling(this._options.SweepTimeout.TotalSeconds + this._options.PollInterval.TotalSeconds + 5)
            };
        }
    }

    /// <summary>Copies nested collections so callers cannot mutate the shared cache.</summary>
    /// <param name="groups">Stored or newly parsed groups.</param>
    /// <returns>Independent group and member arrays.</returns>
    private static TopologyGroup[] CopyGroups(IReadOnlyList<TopologyGroup> groups) =>
        groups.Select(group => group with { Members = group.Members.ToArray() }).ToArray();

    /// <summary>Marks a source stale when no successful observation remains in its freshness window.</summary>
    /// <param name="source">Stored source status.</param>
    /// <param name="now">Read UTC time.</param>
    /// <returns>Status with current staleness.</returns>
    /// <param name="maximumAgeSeconds">Optional diagnostic-specific measurement window.</param>
    private SourceSnapshot WithStaleness(SourceSnapshot source, DateTimeOffset now, double? maximumAgeSeconds = null)
    {
        return source with
        {
            IsStale = source.LastSuccessUtc is null ||
                (now - source.LastSuccessUtc.Value).TotalSeconds > (maximumAgeSeconds ?? this._options.StaleAfter.TotalSeconds)
        };
    }
}
