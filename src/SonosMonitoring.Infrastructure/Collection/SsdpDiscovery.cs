using SonosMonitoring.Application.Collection;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Combines bounded interface searches without expanding the configured unicast host set.</summary>
public sealed class SsdpDiscovery
{
    private readonly CollectorOptions _options;
    private readonly Ipv4TargetPolicy _policy;
    private readonly SsdpSearch _search;
    private readonly CollectorState _state;
    private readonly HashSet<string> _targets;

    /// <summary>Creates optional discovery using the same approved host set as the unicast sweep.</summary>
    /// <param name="options">Validated discovery limits.</param>
    /// <param name="policy">Explicit destination boundaries.</param>
    /// <param name="search">Interface-bound UDP transport.</param>
    /// <param name="state">Independent discovery result cache.</param>
    public SsdpDiscovery(CollectorOptions options, Ipv4TargetPolicy policy, SsdpSearch search, CollectorState state)
    {
        this._options = options;
        this._policy = policy;
        this._search = search;
        this._state = state;
        this._targets = policy.Ranges.SelectMany(range => range.EnumerateHosts(options.MaxScanAddresses)).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Returns deduplicated approved candidates while preserving partial-search diagnostics.</summary>
    /// <param name="cancellationToken">Shared sweep deadline and host shutdown.</param>
    /// <returns>Candidate addresses that still require HTTP player identification.</returns>
    public async Task<IReadOnlyList<string>> DiscoverAsync(CancellationToken cancellationToken)
    {
        if (this._options.Enabled == false || this._options.SsdpEnabled == false)
        {
            return [];
        }

        DateTimeOffset started = DateTimeOffset.UtcNow;
        this._state.SsdpResult(new SsdpSnapshot(true, started, null, 0, 0, false, null));
        try
        {
            SsdpSearchResult[] results = await Task.WhenAll(this._options.SsdpInterfaceAddresses.Select(address =>
                this._search.SearchAsync(address, this._options.SsdpSearchWindow, this._options.SsdpMaxDatagrams, cancellationToken)));
            HashSet<string> candidates = new(StringComparer.Ordinal);
            foreach (SsdpDatagram datagram in results.SelectMany(result => result.Datagrams))
            {
                string? address = SsdpResponseParser.Parse(datagram.Payload, datagram.SenderAddress, this._policy);
                if (address is not null && this._targets.Contains(address))
                {
                    candidates.Add(address);
                }
            }

            int failures = results.Count(result => result.ErrorCode is not null);
            bool limited = results.Any(result => result.LimitReached);
            string? error = failures > 0 ? "socket_failure" : limited ? "response_limit" : null;
            this._state.SsdpResult(new SsdpSnapshot(true, started, DateTimeOffset.UtcNow, candidates.Count, failures, limited, error));
            return candidates.Order(StringComparer.Ordinal).ToArray();
        }
        catch (OperationCanceledException)
        {
            this._state.SsdpResult(new SsdpSnapshot(true, started, null, 0, 0, false, "search_cancelled"));
            throw;
        }
    }
}
