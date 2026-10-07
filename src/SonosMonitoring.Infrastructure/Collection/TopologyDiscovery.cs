using SonosMonitoring.Application.Collection;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Selects recent topology hints within the approved host set with bounded retries and fair rotation.</summary>
public sealed class TopologyDiscovery
{
    private readonly object _gate = new();
    private readonly CollectorOptions _options;
    private readonly HashSet<string> _allowedHosts;
    private readonly Dictionary<string, (DateTimeOffset Next, int Failures)> _retry = new(StringComparer.Ordinal);
    private string? _lastAddress;

    /// <summary>Creates candidate selection over the already validated unicast search scope.</summary>
    /// <param name="options">Resource and polling limits.</param>
    /// <param name="policy">Explicit allowed ranges.</param>
    public TopologyDiscovery(CollectorOptions options, Ipv4TargetPolicy policy)
    {
        this._options = options;
        this._allowedHosts = policy.Ranges.SelectMany(range => range.EnumerateHosts(options.MaxScanAddresses)).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Uses only fresh observations from currently validated reporters, retaining no unbounded work queue.</summary>
    /// <param name="snapshot">Current source quality and reported groups.</param>
    /// <param name="scheduled">Known and seeded addresses already due in this pass.</param>
    /// <param name="now">Selection time used for retry deadlines.</param>
    /// <returns>A capped, rotating selection of address hints; advertised UDNs never determine discovered identities.</returns>
    public TopologyCandidateSelection Select(CollectorSnapshot snapshot, IReadOnlySet<string> scheduled, DateTimeOffset now)
    {
        lock (this._gate)
        {
            int maximum = Math.Min(this._options.MaxTopologyCandidates, this._options.MaxScanAddresses);
            string[] eligible = snapshot.Devices
                .Where(device => device.Description.LastAttemptSucceeded && device.Description.IsStale == false &&
                    device.TopologyStatus.LastAttemptSucceeded && device.TopologyStatus.LastSuccessUtc.HasValue && device.TopologyStatus.IsStale == false)
                .SelectMany(device => device.Groups).SelectMany(group => group.Members)
                .Select(member => member.ReportedAddress)
                .OfType<string>()
                .Where(address => this._allowedHosts.Contains(address) && scheduled.Contains(address) == false &&
                    (this._retry.TryGetValue(address, out var retry) == false || now >= retry.Next))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(address => this._lastAddress is not null && StringComparer.Ordinal.Compare(address, this._lastAddress) <= 0 ? 1 : 0)
                .ThenBy(address => address, StringComparer.Ordinal)
                .Take(maximum + 1).ToArray();
            string[] selected = eligible.Take(maximum).ToArray();
            foreach (string address in selected)
            {
                this._retry.TryGetValue(address, out var previous);
                this._retry[address] = (now + this._options.PollInterval, previous.Failures);
            }
            if (selected.Length > 0)
            {
                this._lastAddress = selected[^1];
            }

            return new TopologyCandidateSelection(selected, eligible.Length > maximum);
        }
    }

    /// <summary>Backs off unsuccessful hints and releases successful ones to normal known-device polling.</summary>
    /// <param name="address">A selected address whose description probe completed.</param>
    /// <param name="found">Whether the description identified a valid Sonos player, regardless of advertised UDN.</param>
    /// <param name="now">Probe completion time.</param>
    public void RecordResult(string address, bool found, DateTimeOffset now)
    {
        lock (this._gate)
        {
            if (this._retry.TryGetValue(address, out var previous) == false)
            {
                return;
            }

            if (found)
            {
                this._retry.Remove(address);
                return;
            }

            int failures = Math.Min(previous.Failures + 1, 5);
            double delay = Math.Min(300, this._options.PollInterval.TotalSeconds * Math.Pow(2, failures));
            this._retry[address] = (now + TimeSpan.FromSeconds(delay + Random.Shared.NextDouble()), failures);
        }
    }
}
