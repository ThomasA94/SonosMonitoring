using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SonosMonitoring.Application.Collection;
using System.Diagnostics;
using SonosMonitoring.Infrastructure.Collection.Diagnostics;
using System.Net;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Runs bounded device discovery and diagnostic polling independently of UI requests.</summary>
public sealed class CollectorWorker : BackgroundService
{
    private readonly CollectorOptions _options;
    private readonly Ipv4TargetPolicy _policy;
    private readonly PlayerProbe _probe;
    private readonly DeviceDiagnosticsCollector _diagnostics;
    private readonly TopologyCollector _topology;
    private readonly TopologyDiscovery _topologyDiscovery;
    private readonly SsdpDiscovery _ssdp;
    private readonly CollectorState _state;
    private readonly ILogger<CollectorWorker> _logger;
    private readonly IDeviceCollectionPolicy? _collectionPolicy;
    private readonly DiscoveryTargetScheduler _discoveryScheduler = new();

    /// <summary>Creates the background collector.</summary>
    /// <param name="options">Collection limits and intervals.</param>
    /// <param name="policy">Explicit target allowlist.</param>
    /// <param name="probe">Read-only device transport.</param>
    /// <param name="topology">Independent topology polling and backoff.</param>
    /// <param name="topologyDiscovery">Bounded topology address-hint selection.</param>
    /// <param name="ssdp">Optional interface-bound discovery.</param>
    /// <param name="state">Live state cache.</param>
    /// <param name="logger">Diagnostic logger.</param>
    /// <param name="collectionPolicy">Optional application monitoring policy.</param>
    /// <param name="diagnostics">Independent source scheduler, or a generation-owned default for direct construction.</param>
    public CollectorWorker(CollectorOptions options, Ipv4TargetPolicy policy, PlayerProbe probe, TopologyCollector topology, TopologyDiscovery topologyDiscovery, SsdpDiscovery ssdp,
        CollectorState state, ILogger<CollectorWorker> logger, IDeviceCollectionPolicy? collectionPolicy = null, DeviceDiagnosticsCollector? diagnostics = null)
    {
        this._options = options;
        this._policy = policy;
        this._probe = probe;
        this._diagnostics = diagnostics ?? new DeviceDiagnosticsCollector(probe, state, options);
        this._topology = topology;
        this._topologyDiscovery = topologyDiscovery;
        this._ssdp = ssdp;
        this._state = state;
        this._logger = logger;
        this._collectionPolicy = collectionPolicy;
    }

    /// <summary>Runs known-address polls and periodic full CIDR sweeps until shutdown.</summary>
    /// <param name="stoppingToken">Host shutdown token.</param>
    /// <returns>A task that completes on shutdown.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (this._options.Enabled == false || this._policy.Ranges.Count == 0)
        {
            return;
        }

        DateTimeOffset nextDiscovery = DateTimeOffset.MinValue;
        using PeriodicTimer timer = new(this._options.PollInterval);
        do
        {
            this._state.Heartbeat(DateTimeOffset.UtcNow);
            bool fullScan = DateTimeOffset.UtcNow >= nextDiscovery;
            using CancellationTokenSource sweep = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            sweep.CancelAfter(this._options.SweepTimeout);
            try
            {
                Stopwatch sweepWatch = Stopwatch.StartNew();
                await this.CollectAsync(fullScan, sweep.Token, stoppingToken);
                this._state.SweepCompleted(DateTimeOffset.UtcNow, sweepWatch.Elapsed.TotalSeconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                this._logger.LogWarning("Sonos collection pass exceeded its time budget.");
            }
            catch (Exception exception)
            {
                this._logger.LogError(exception, "Sonos collection pass failed.");
            }

            if (fullScan)
            {
                nextDiscovery = DateTimeOffset.UtcNow + this._options.DiscoveryInterval;
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Builds a deduplicated target set and probes it within the shared time budget.</summary>
    /// <param name="fullScan">Whether to include every configured CIDR.</param>
    /// <param name="cancellationToken">Sweep deadline and host shutdown.</param>
    /// <param name="stoppingToken">Host shutdown, distinguished from a failed sweep.</param>
    /// <returns>A task for the collection pass.</returns>
    internal async Task CollectAsync(bool fullScan, CancellationToken cancellationToken, CancellationToken stoppingToken)
    {
        HashSet<string> targets = new(StringComparer.Ordinal);
        foreach (string seed in this._options.SeedAddresses)
        {
            targets.Add(seed);
        }

        CollectorSnapshot snapshot = this._state.GetSnapshot();
        foreach (DeviceSnapshot device in snapshot.Devices)
        {
            if (this._policy.IsAllowed(device.Address))
            {
                targets.Add(device.Address);
            }
        }

        TopologyCandidateSelection topologyCandidates = this._topologyDiscovery.Select(snapshot, targets, DateTimeOffset.UtcNow);
        this._state.TopologyCandidatesSelected(topologyCandidates, DateTimeOffset.UtcNow);
        foreach (string candidate in topologyCandidates.Addresses)
        {
            targets.Add(candidate);
        }

        List<(string Cidr, string Address)> rangeTargets = [];
        IReadOnlyList<DiscoveryTarget> plan;
        if (fullScan)
        {
            foreach (string candidate in await this._ssdp.DiscoverAsync(cancellationToken))
            {
                targets.Add(candidate);
            }

            string[] knownTargets = targets.ToArray();
            List<(string Cidr, string[] Hosts)> ranges = this._policy.Ranges
                .Select(range => (range.ToString(), range.EnumerateHosts(this._options.MaxScanAddresses).ToArray()))
                .ToList();
            int longest = ranges.Max(range => range.Hosts.Length);
            for (int index = 0; index < longest; index++)
            {
                foreach ((string cidr, string[] hosts) in ranges)
                {
                    if (index < hosts.Length)
                    {
                        string address = hosts[index];
                        rangeTargets.Add((cidr, address));
                        targets.Add(address);
                    }
                }
            }

            plan = this._discoveryScheduler.CreatePlan(knownTargets, rangeTargets);
        }
        else
        {
            plan = targets.Select(address => new DiscoveryTarget(address, null, 0)).ToArray();
        }

        if (targets.Count > this._options.MaxScanAddresses)
        {
            throw new InvalidOperationException("Configured scan targets exceed MaxScanAddresses.");
        }

        DateTimeOffset started = DateTimeOffset.UtcNow;
        System.Collections.Concurrent.ConcurrentDictionary<string, (bool Found, string? Error)> found = new(StringComparer.Ordinal);
        System.Collections.Concurrent.ConcurrentDictionary<string, byte> startedTargets = new(StringComparer.Ordinal);
        try
        {
            await Parallel.ForEachAsync(plan, new ParallelOptions
            {
                MaxDegreeOfParallelism = this._options.MaxParallelism,
                CancellationToken = cancellationToken
            }, async (target, token) =>
            {
                string address = target.Address;
                startedTargets.TryAdd(address, 0);
                bool success = await this.ProbeAddressAsync(address,
                    (descriptionFound, error) => found[address] = (descriptionFound, error), token, stoppingToken);
                this._topologyDiscovery.RecordResult(address, success, DateTimeOffset.UtcNow);
            });
        }
        finally
        {
            if (stoppingToken.IsCancellationRequested == false)
            {
                CollectorNetworkDiagnostics.LogTransitions(this._logger, snapshot.Devices, this._state.GetSnapshot().Devices);
            }

            if (fullScan)
            {
                this._discoveryScheduler.RecordStarted(plan, startedTargets.Count);
            }

            if (fullScan)
            {
                int devicesFound = found.Count(item => item.Value.Found);
                this._state.DiscoveryResult(found.Count, devicesFound);
                if (stoppingToken.IsCancellationRequested == false)
                {
                    CollectorNetworkDiagnostics.LogDiscovery(this._logger,
                        this._policy.Ranges.Select(range => range.ToString()).ToArray(), targets.Count, found.Count,
                        devicesFound, found.Count(item => item.Value.Error == "timeout"), cancellationToken.IsCancellationRequested);
                }

                foreach (IGrouping<string, (string Cidr, string Address)> group in rangeTargets.GroupBy(item => item.Cidr))
                {
                    int probed = group.Count(item => found.ContainsKey(item.Address));
                    int devices = group.Count(item => found.TryGetValue(item.Address, out var value) && value.Found);
                    string? error = stoppingToken.IsCancellationRequested ? "cancelled" : cancellationToken.IsCancellationRequested ? "sweep_timeout" : null;
                    this._state.RangeResult(group.Key, started, error is null ? DateTimeOffset.UtcNow : null,
                        probed, devices, error);
                }
            }
        }
    }

    /// <summary>Reads identity and diagnostics independently for one allowed address.</summary>
    /// <param name="address">Literal IPv4 address.</param>
    /// <param name="descriptionCompleted">Records identity results before optional diagnostics can fail or time out.</param>
    /// <param name="cancellationToken">Sweep deadline and host shutdown.</param>
    /// <param name="stoppingToken">Host shutdown, which must not be recorded as lost reachability.</param>
    /// <returns>Whether a valid Sonos player was found.</returns>
    private async Task<bool> ProbeAddressAsync(string address, Action<bool, string?> descriptionCompleted,
        CancellationToken cancellationToken, CancellationToken stoppingToken)
    {
        PlayerDescription description;
        Stopwatch probeWatch = Stopwatch.StartNew();
        string? probeError = null;
        try
        {
            string xml = await this.GetWithRetryAsync(address, "/xml/device_description.xml", cancellationToken);
            description = PlayerDescriptionParser.Parse(xml);
            descriptionCompleted(true, null);
            if (this._state.DescriptionSucceeded(description, address, DateTimeOffset.UtcNow) == false)
            {
                return true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (stoppingToken.IsCancellationRequested == false)
            {
                probeError = "timeout";
                this._state.DescriptionFailed(address, DateTimeOffset.UtcNow, probeError);
                descriptionCompleted(false, probeError);
            }
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or InvalidDataException or FormatException)
        {
            probeError = this.ErrorCode(exception);
            this._state.DescriptionFailed(address, DateTimeOffset.UtcNow, probeError);
            descriptionCompleted(false, probeError);
            return false;
        }

        finally
        {
            this._state.DescriptionProbeFinished(address, probeWatch.Elapsed.TotalSeconds, probeError);
        }

        await this._topology.CollectAsync(description.DeviceId, address, cancellationToken);

        CollectionDecision decision;
        try
        {
            decision = this._collectionPolicy is null
                ? new CollectionDecision(true, this._options.PollInterval)
                : await this._collectionPolicy.GetDecisionAsync(description.DeviceId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this._logger.LogWarning(exception, "Monitoring decision is unavailable for player {DeviceId}.", description.DeviceId);
            this._state.DiagnosticDecision(description.DeviceId, null, null, DateTimeOffset.UtcNow);
            return true;
        }

        if (decision.Enabled == false)
        {
            this._state.DiagnosticDecision(description.DeviceId, false, Math.Max(decision.PollInterval.TotalSeconds, this._options.PollInterval.TotalSeconds), DateTimeOffset.UtcNow);
            return true;
        }

        TimeSpan interval = decision.PollInterval < this._options.PollInterval
            ? this._options.PollInterval : decision.PollInterval;
        this._state.DiagnosticDecision(description.DeviceId, true, interval.TotalSeconds, DateTimeOffset.UtcNow);
        await this._diagnostics.CollectAsync(description, address, interval, cancellationToken);

        return true;
    }

    /// <summary>Retries one transient request with jitter inside the shared sweep deadline.</summary>
    /// <param name="address">Approved literal target.</param>
    /// <param name="path">Fixed read-only path.</param>
    /// <param name="cancellationToken">Sweep cancellation.</param>
    /// <returns>Bounded response body.</returns>
    private async Task<string> GetWithRetryAsync(string address, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await this._probe.GetAsync(address, path, cancellationToken);
        }
        catch (Exception exception) when (this.IsTransient(exception) &&
            cancellationToken.IsCancellationRequested == false)
        {
            TimeSpan requested = (exception as PlayerHttpException)?.RetryDelay ?? TimeSpan.Zero;
            double delayMilliseconds = Math.Clamp(requested.TotalMilliseconds, 100, 2000) + Random.Shared.Next(0, 101);
            await Task.Delay(TimeSpan.FromMilliseconds(delayMilliseconds), cancellationToken);
            return await this._probe.GetAsync(address, path, cancellationToken);
        }
    }

    /// <summary>Retries only transport timeouts and HTTP 408, 429, or server failures.</summary>
    /// <param name="exception">First request failure.</param>
    /// <returns>Whether one retry is justified.</returns>
    private bool IsTransient(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return true;
        }

        if (exception is HttpRequestException requestException)
        {
            HttpStatusCode? status = requestException.StatusCode;
            return status is null || status == HttpStatusCode.RequestTimeout ||
                status == HttpStatusCode.TooManyRequests || (int)status >= 500;
        }

        return false;
    }

    /// <summary>Converts transport failures to bounded, non-sensitive status codes.</summary>
    /// <param name="exception">Observed failure.</param>
    /// <returns>Stable category without exception details.</returns>
    private string ErrorCode(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => "timeout",
            HttpRequestException => "http_failure",
            InvalidDataException => "response_too_large",
            IOException => "http_failure",
            FormatException => "invalid_response",
            _ => "probe_failure"
        };
    }
}
