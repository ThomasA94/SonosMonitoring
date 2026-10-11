using Microsoft.Extensions.Logging.Abstractions;
using SonosMonitoring.Infrastructure.Collection;
using SonosMonitoring.Infrastructure.Collection.Diagnostics;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Verifies discovery continues through range hosts when every pass is interrupted.</summary>
public sealed class DiscoveryTargetSchedulerTests
{
    /// <summary>Rotates past a hanging first host across interrupted sweeps and reaches a later player.</summary>
    [Fact]
    public async Task InterruptedSweepsEventuallyProbeLateHealthyHostWithinLimits()
    {
        CollectorOptions options = new()
        {
            Enabled = true,
            AllowedCidrs = ["192.168.50.0/29"],
            SeedAddresses = ["192.168.50.1"],
            PollInterval = TimeSpan.FromSeconds(15),
            MaxScanAddresses = 16,
            MaxParallelism = 2,
            RequestTimeout = TimeSpan.FromSeconds(2),
            SweepTimeout = TimeSpan.FromMilliseconds(100)
        };
        Ipv4TargetPolicy policy = new(options.AllowedCidrs, false);
        using DiscoveryHandler handler = new();
        using PlayerProbe probe = new(policy, options, handler);
        CollectorState state = new(options);
        TopologyDiscovery topologyDiscovery = new(options, policy);
        SsdpDiscovery ssdp = new(options, policy, new SsdpSearch(), state);
        CollectorWorker worker = new(options, policy, probe, new TopologyCollector(probe, state, options),
            topologyDiscovery, ssdp, state, NullLogger<CollectorWorker>.Instance,
            diagnostics: new DeviceDiagnosticsCollector(probe, state, options));

        using CancellationTokenSource globalBudget = new(TimeSpan.FromSeconds(10));
        List<string[]> identityAttemptsBySweep = [];
        for (int sweepNumber = 0; sweepNumber < 8 && state.GetSnapshot().Devices.Count == 0; sweepNumber++)
        {
            int requestStart = handler.IdentityRequests.Count;
            using CancellationTokenSource sweep = CancellationTokenSource.CreateLinkedTokenSource(globalBudget.Token);
            Task collection = worker.CollectAsync(true, sweep.Token, CancellationToken.None);
            try
            {
                await handler.WaitForIdentityRequestsAsync(requestStart + 2, globalBudget.Token);
                if (handler.IdentityRequests.Skip(requestStart).Contains("192.168.50.6", StringComparer.Ordinal))
                {
                    await WaitForDiscoveredPlayerAsync(state, globalBudget.Token);
                }
            }
            catch
            {
                sweep.Cancel();
                try
                {
                    await collection;
                }
                catch (OperationCanceledException) when (sweep.IsCancellationRequested)
                {
                }

                throw;
            }

            sweep.Cancel();
            try
            {
                await collection;
            }
            catch (OperationCanceledException) when (sweep.IsCancellationRequested)
            {
            }
            identityAttemptsBySweep.Add(handler.IdentityRequests.Skip(requestStart).ToArray());
        }

        Assert.Contains("192.168.50.6", handler.IdentityRequests);
        Assert.NotEmpty(state.GetSnapshot().Devices);
        Assert.InRange(identityAttemptsBySweep.Count, 2, 5);
        Assert.Contains("192.168.50.1", identityAttemptsBySweep[0]);
        Assert.DoesNotContain("192.168.50.1", identityAttemptsBySweep[1]);
        Assert.Contains("192.168.50.3", identityAttemptsBySweep[1]);
        Assert.Contains("192.168.50.4", identityAttemptsBySweep[1]);
        Assert.Equal(2, identityAttemptsBySweep[0].Length);
        Assert.Equal(2, handler.MaximumConcurrentRequests);
        Assert.All(identityAttemptsBySweep, attempts => Assert.Equal(attempts.Length, attempts.Distinct(StringComparer.Ordinal).Count()));
    }

    /// <summary>Waits until the healthy identity has been added to the live snapshot.</summary>
    /// <param name="state">Collector cache updated by the identity probe.</param>
    /// <param name="ct">Global bound for the deterministic simulation.</param>
    private static async Task WaitForDiscoveredPlayerAsync(CollectorState state, CancellationToken ct)
    {
        while (state.GetSnapshot().Devices.Count == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), ct);
        }
    }

    /// <summary>Ensures overlapping known seeds cannot keep discovery at the same range index.</summary>
    /// <param name="probesPerSweep">Number of probes allowed to start before each interrupted sweep.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AllKnownSeedsOverlappingRangeAdvanceFairly(int probesPerSweep)
    {
        DiscoveryTargetScheduler scheduler = new();
        string[] hosts = Enumerable.Range(1, 6).Select(index => $"192.168.50.{index}").ToArray();
        string[] known = [.. hosts.Reverse()];
        (string Cidr, string Address)[] ranges = hosts.Select(address => ("192.168.50.0/29", address)).ToArray();
        HashSet<string> reached = new(StringComparer.Ordinal);

        for (int sweep = 0; sweep < hosts.Length && reached.Count < hosts.Length; sweep++)
        {
            IReadOnlyList<DiscoveryTarget> plan = scheduler.CreatePlan(known, ranges);
            Assert.Equal(plan.Count, plan.Select(target => target.Address).Distinct(StringComparer.Ordinal).Count());
            foreach (DiscoveryTarget target in plan.Take(probesPerSweep))
            {
                reached.Add(target.Address);
            }

            scheduler.RecordStarted(plan, Math.Min(probesPerSweep, plan.Count));
        }

        Assert.Equal(hosts, reached.Order(StringComparer.Ordinal).ToArray());
    }
}
