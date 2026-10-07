using System.Net;
using System.Net.Http.Json;
using SonosMonitoring.Api;
using SonosMonitoring.Contracts.Collection;
using SonosMonitoring.Infrastructure.Collection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SonosMonitoring.Api.Collection;

namespace SonosMonitoring.E2E.Tests.Collection;

/// <summary>Verifies real HTTP collection, fault isolation, visible stale data and automatic recovery.</summary>
[Collection("Loopback player ports")]
public sealed class CollectorJourneyTests
{
    /// <summary>A failing device never stops its healthy peer and recovers without an API restart.</summary>
    /// <param name="fault">The simulator failure to inject.</param>
    [Theory]
    [InlineData("timeout")]
    [InlineData("disconnect")]
    [InlineData("429")]
    [InlineData("503")]
    [InlineData("oversize")]
    [InlineData("invalid")]
    [InlineData("xml")]
    [InlineData("description_disconnect")]
    public async Task FaultsAreIsolatedAndRecoveryPreservesObservationTimes(string fault)
    {
        await using var players = new SimulatedPlayer();
        await players.StartAsync();
        await using var api = ApiHost.Build([], new Dictionary<string, string?>
        {
            ["CollectionSettings:Path"] = Path.Combine(Path.GetTempPath(), "sonos-isolated-" + Guid.NewGuid().ToString("N"), "settings.json"),
            ["Backend:Enabled"] = "false",
            ["Environment"] = "Testing",
            ["Collector:AllowLoopback"] = "true",
            ["Collector:Enabled"] = "true",
            ["Collector:AllowedCidrs:0"] = "127.0.0.2/32",
            ["Collector:AllowedCidrs:1"] = "127.0.0.3/32",
            ["Collector:PollInterval"] = "00:00:01",
            // Retry undiscovered players inside the observation deadline if their first connection is delayed.
            ["Collector:DiscoveryInterval"] = "00:00:05",
            ["Collector:StaleAfter"] = "00:00:02",
            // Leave scheduler headroom on shared CI hosts; injected delays still exceed this budget tenfold.
            ["Collector:RequestTimeout"] = "00:00:01",
            ["Collector:SweepTimeout"] = "00:00:05",
            ["Collector:MaxResponseBytes"] = "4096",
        });
        api.Urls.Clear();
        api.Urls.Add("http://127.0.0.1:0");
        await api.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(api.Urls.Single()) };
        var initial = await UntilAsync(client, snapshot => snapshot.Devices.Count == 2 &&
            snapshot.Devices.All(device => device.Interfaces.Count == 1 &&
                device.Description.LastAttemptSucceeded && device.InterfaceStatus.LastAttemptSucceeded));
        var original = initial.Devices.Single(device => device.DeviceId == "RINCON_SECOND");
        players.Fault = fault;
        bool descriptionFault = fault is "xml" or "description_disconnect";
        // A successful response already in flight can publish after fault injection. Once a failure
        // is observed, later failed attempts must retain that last good timestamp and counter value.
        var firstFailure = await UntilAsync(client, snapshot =>
        {
            var bad = snapshot.Devices.Single(device => device.DeviceId == "RINCON_SECOND");
            return descriptionFault ? bad.Description.LastAttemptSucceeded == false : bad.InterfaceStatus.LastAttemptSucceeded == false;
        });
        var failureBaseline = firstFailure.Devices.Single(device => device.DeviceId == "RINCON_SECOND");
        Assert.True(failureBaseline.InterfaceStatus.LastSuccessUtc >= original.InterfaceStatus.LastSuccessUtc);
        Assert.True(failureBaseline.Interfaces[0].ReceiveBytes >= original.Interfaces[0].ReceiveBytes);
        var failed = await UntilAsync(client, snapshot =>
        {
            var bad = snapshot.Devices.Single(device => device.DeviceId == "RINCON_SECOND");
            var peer = snapshot.Devices.Single(device => device.DeviceId == "RINCON_FIRST");
            var failedSource = descriptionFault ? bad.Description : bad.InterfaceStatus;
            var baselineSource = descriptionFault ? failureBaseline.Description : failureBaseline.InterfaceStatus;
            // Independent source updates can become visible in either order within one sweep.
            return bad.InterfaceStatus.IsStale &&
                peer.InterfaceStatus.LastSuccessUtc > original.InterfaceStatus.LastSuccessUtc &&
                peer.InterfaceStatus.IsStale == false &&
                failedSource.LastAttemptSucceeded == false && failedSource.LastAttemptUtc > baselineSource.LastAttemptUtc;
        });
        var retained = failed.Devices.Single(device => device.DeviceId == "RINCON_SECOND");
        Assert.Equal(failureBaseline.InterfaceStatus.LastSuccessUtc, retained.InterfaceStatus.LastSuccessUtc);
        Assert.Equal(failureBaseline.Interfaces[0].ReceiveBytes, retained.Interfaces[0].ReceiveBytes);
        var healthy = failed.Devices.Single(device => device.DeviceId == "RINCON_FIRST");
        Assert.True(healthy.InterfaceStatus.LastSuccessUtc > original.InterfaceStatus.LastSuccessUtc);
        Assert.False(healthy.InterfaceStatus.IsStale);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        var metrics = await client.GetStringAsync("/metrics");
        Assert.DoesNotContain("RINCON_", metrics);
        Assert.Contains("\nsonos_collector_enabled 1\n", metrics);
        Assert.Contains("\nsonos_collector_heartbeat_max_age_seconds 11\n", metrics);
        string deviceLabel = CollectorMetricsFormatter.DeviceLabel("RINCON_SECOND");
        int expectedProbeSuccess = fault is "xml" or "description_disconnect" ? 0 : 1;
        Assert.Contains($"sonos_device_probe_success{{device_id=\"{deviceLabel}\"}} {expectedProbeSuccess}\n", metrics);
        players.Fault = "";
        var recovered = await UntilAsync(client, snapshot =>
        {
            var second = snapshot.Devices.Single(device => device.DeviceId == "RINCON_SECOND");
            return second.InterfaceStatus.LastSuccessUtc > retained.InterfaceStatus.LastSuccessUtc && second.InterfaceStatus.IsStale == false;
        });
        Assert.True(recovered.Devices.Single(device => device.DeviceId == "RINCON_SECOND").Interfaces[0].ReceiveBytes > retained.Interfaces[0].ReceiveBytes);
        await api.StopAsync();
    }

    /// <summary>Preserves stale topology on source failures while counters and peer topology keep updating.</summary>
    /// <param name="fault">Independent topology failure injected on the second player.</param>
    [Theory]
    [InlineData("timeout")]
    [InlineData("503")]
    [InlineData("redirect")]
    [InlineData("oversize")]
    [InlineData("invalid")]
    [InlineData("soap_fault")]
    [InlineData("disconnect")]
    public async Task TopologyChangesAndFailuresRemainIndependent(string fault)
    {
        await using var players = new SimulatedPlayer();
        await players.StartAsync();
        await using var api = ApiHost.Build([], new Dictionary<string, string?>
        {
            ["CollectionSettings:Path"] = Path.Combine(Path.GetTempPath(), "sonos-isolated-" + Guid.NewGuid().ToString("N"), "settings.json"),
            ["Backend:Enabled"] = "false",
            ["Environment"] = "Testing",
            ["Collector:AllowLoopback"] = "true",
            ["Collector:Enabled"] = "true",
            ["Collector:AllowedCidrs:0"] = "127.0.0.2/32",
            ["Collector:AllowedCidrs:1"] = "127.0.0.3/32",
            ["Collector:PollInterval"] = "00:00:01",
            ["Collector:DiscoveryInterval"] = "00:00:05",
            ["Collector:StaleAfter"] = "00:00:03",
            ["Collector:RequestTimeout"] = "00:00:01",
            ["Collector:SweepTimeout"] = "00:00:05",
            ["Collector:MaxResponseBytes"] = "4096"
        });
        api.Urls.Clear();
        api.Urls.Add("http://127.0.0.1:0");
        await api.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(api.Urls.Single()) };
        var initial = await UntilAsync(client, snapshot => snapshot.Devices.Count == 2 &&
            snapshot.Devices.All(device => device.Groups.Count == 1 && device.Interfaces.Count == 1));
        var original = initial.Devices.Single(device => device.DeviceId == "RINCON_SECOND");
        var group = Assert.Single(original.Groups);
        Assert.Equal("RINCON_FIRST", group.CoordinatorDeviceId);
        Assert.Equal("RINCON_FIRST", group.Members.Single(member => member.DeviceId == "RINCON_SATELLITE").ParentDeviceId);
        players.TopologyFault = fault;
        players.SplitGroups = true;
        var failed = await UntilAsync(client, snapshot =>
        {
            var second = snapshot.Devices.Single(device => device.DeviceId == "RINCON_SECOND");
            var first = snapshot.Devices.Single(device => device.DeviceId == "RINCON_FIRST");
            return second.TopologyStatus.LastAttemptSucceeded == false && second.TopologyStatus.IsStale &&
                second.InterfaceStatus.LastSuccessUtc > original.InterfaceStatus.LastSuccessUtc && first.Groups.Count == 2;
        });
        var retained = failed.Devices.Single(device => device.DeviceId == "RINCON_SECOND");
        Assert.Equal(original.TopologyStatus.LastSuccessUtc, retained.TopologyStatus.LastSuccessUtc);
        Assert.Equal("together", Assert.Single(retained.Groups).GroupId);
        Assert.True(retained.InterfaceStatus.LastAttemptSucceeded);
        Assert.False(retained.InterfaceStatus.IsStale);
        Assert.False(failed.Devices.Single(device => device.DeviceId == "RINCON_FIRST").TopologyStatus.IsStale);
        players.TopologyFault = "";
        var recovered = await UntilAsync(client, snapshot => snapshot.Devices.All(device =>
            device.Groups.Count == 2 && device.TopologyStatus.LastAttemptSucceeded && device.TopologyStatus.IsStale == false));
        Assert.True(recovered.Devices.Single(device => device.DeviceId == "RINCON_SECOND").TopologyStatus.LastSuccessUtc > original.TopologyStatus.LastSuccessUtc);
        Assert.Equal(0, players.UnexpectedRequests);
        await api.StopAsync();
    }

    /// <summary>Coalesces concurrent addresses for one UDN and respects the successful poll interval.</summary>
    [Fact]
    public async Task TopologyPollsOneIdentityAtMostOncePerInterval()
    {
        await using var players = new SimulatedPlayer();
        await players.StartAsync();
        CollectorOptions options = new() { PollInterval = TimeSpan.FromMinutes(1) };
        CollectorState state = new(options);
        state.DescriptionSucceeded(new PlayerDescription("RINCON_FIRST", null, null), "127.0.0.2", DateTimeOffset.UtcNow);
        using PlayerProbe probe = new(new Ipv4TargetPolicy(["127.0.0.2/31"], true), options);
        TopologyCollector collector = new(probe, state, options);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            collector.CollectAsync("RINCON_FIRST", index % 2 == 0 ? "127.0.0.2" : "127.0.0.3", CancellationToken.None)));
        await collector.CollectAsync("RINCON_FIRST", "127.0.0.2", CancellationToken.None);
        Assert.True(Assert.Single(state.GetSnapshot().Devices).TopologyStatus.LastAttemptSucceeded);
        Assert.Equal(1, players.TopologyRequests);
    }

    /// <summary>SSDP candidates are validated through HTTP and never suppress scanning the remaining approved ranges.</summary>
    /// <param name="advertise">Whether the UDP simulator returns a duplicate valid advertisement and an untrusted URL.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SsdpCandidatesDoNotReplaceTheFullUnicastSearch(bool advertise)
    {
        await using var players = new SimulatedPlayer();
        await players.StartAsync();
        await using SimulatedSsdpResponder responder = advertise
            ? new(SimulatedSsdpResponder.Reply(), SimulatedSsdpResponder.Reply(), SimulatedSsdpResponder.Reply("127.0.0.4"))
            : new();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSonosCollector(options =>
        {
            options.Enabled = true;
            options.AllowLoopback = true;
            options.AllowedCidrs = ["127.0.0.2/32", "127.0.0.3/32"];
            options.SsdpEnabled = true;
            options.SsdpInterfaceAddresses = ["127.0.0.1"];
            options.SsdpSearchWindow = TimeSpan.FromSeconds(2);
            options.SweepTimeout = TimeSpan.FromSeconds(10);
            options.RequestTimeout = TimeSpan.FromSeconds(1);
            options.PollInterval = TimeSpan.FromSeconds(1);
            options.DiscoveryInterval = TimeSpan.FromSeconds(5);
        });
        // Only the transport destination changes: the real worker, parser, HTTP probes and API mapping run unchanged.
        builder.Services.AddSingleton(new SsdpSearch(responder.Endpoint));
        await using var api = builder.Build();
        api.MapCollection();
        await api.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(api.Urls.Single()) };
        var snapshot = await UntilAsync(client, current => current.Devices.Count == 2 && current.LastSweepUtc.HasValue);
        Assert.True(snapshot.Ssdp.Enabled);
        Assert.Equal(advertise ? 1 : 0, snapshot.Ssdp.CandidateCount);
        Assert.NotNull(snapshot.Ssdp.LastCompletedUtc);
        Assert.Null(snapshot.Ssdp.ErrorCode);
        Assert.Equal(2, snapshot.Ranges.Count);
        Assert.All(snapshot.Ranges, range => Assert.Equal(1, range.DevicesFound));
        Assert.Contains(snapshot.Devices, device => device.DeviceId == "RINCON_SECOND");
        Assert.Equal(0, players.UnexpectedRequests);
        await api.StopAsync();
    }

    /// <summary>Finds new addresses and IP changes between full sweeps without trusting advertised identities or escaping CIDRs.</summary>
    [Fact]
    public async Task TopologyHintsDiscoverAddressChangesUsingActualHttpIdentity()
    {
        await using var players = new SimulatedPlayer { SecondAvailable = false };
        await players.StartAsync();
        await using var api = ApiHost.Build([], new Dictionary<string, string?>
        {
            ["CollectionSettings:Path"] = Path.Combine(Path.GetTempPath(), "sonos-isolated-" + Guid.NewGuid().ToString("N"), "settings.json"),
            ["Backend:Enabled"] = "false",
            ["Environment"] = "Testing",
            ["Collector:AllowLoopback"] = "true",
            ["Collector:Enabled"] = "true",
            ["Collector:AllowedCidrs:0"] = "127.0.0.2/32",
            ["Collector:AllowedCidrs:1"] = "127.0.0.3/32",
            ["Collector:AllowedCidrs:2"] = "127.0.0.4/32",
            ["Collector:PollInterval"] = "00:00:01",
            ["Collector:DiscoveryInterval"] = "00:05:00",
            ["Collector:StaleAfter"] = "00:00:10",
            ["Collector:RequestTimeout"] = "00:00:01",
            ["Collector:SweepTimeout"] = "00:00:10"
        });
        api.Urls.Clear();
        api.Urls.Add("http://127.0.0.1:0");
        await api.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(api.Urls.Single()) };
        var initial = await UntilAsync(client, snapshot => snapshot.LastSweepUtc.HasValue && snapshot.Devices.Count == 1);
        Assert.Equal(3, initial.Ranges.Count);
        Assert.All(initial.Ranges, range => Assert.Equal(1, range.AddressesProbed));

        players.ReportedSecondId = "RINCON_UNTRUSTED_CLAIM";
        players.ExtraTopologyLocation = "http://127.0.0.5:1400/xml/device_description.xml";
        players.SecondAvailable = true;
        players.ReportedSecondLocation = "http://127.0.0.3:1400/xml/device_description.xml";
        var discovered = await UntilAsync(client, snapshot => snapshot.Devices.Any(device => device.DeviceId == "RINCON_SECOND"));
        var original = discovered.Devices.Single(device => device.DeviceId == "RINCON_SECOND");
        Assert.Equal("127.0.0.3", original.Address);
        Assert.DoesNotContain(discovered.Devices, device => device.DeviceId == "RINCON_UNTRUSTED_CLAIM");
        Assert.Contains(discovered.Devices.Single(device => device.DeviceId == "RINCON_FIRST").Groups.SelectMany(group => group.Members),
            member => member.DeviceId == "RINCON_OUTSIDE" && member.ReportedAddress == "127.0.0.5");

        players.SecondAddress = "127.0.0.4";
        players.ReportedSecondLocation = "http://127.0.0.4:1400/xml/device_description.xml";
        var moved = await UntilAsync(client, snapshot => snapshot.Devices.Any(device => device.DeviceId == "RINCON_SECOND" && device.Address == "127.0.0.4"));
        Assert.Equal(2, moved.Devices.Count);
        Assert.Equal(original.FirstSeenUtc, moved.Devices.Single(device => device.DeviceId == "RINCON_SECOND").FirstSeenUtc);
        Assert.Equal(initial.Ranges.Select(range => range.LastAttemptUtc), moved.Ranges.Select(range => range.LastAttemptUtc));
        Assert.NotNull(moved.TopologyDiscovery.LastSelectionUtc);
        Assert.Equal(0, players.BlockedTargetRequests);
        Assert.Equal(0, players.UnexpectedRequests);
        await api.StopAsync();
    }

    /// <summary>Observes asynchronously updated state without relying on a fixed sleep duration.</summary>
    /// <param name="client">Live API client.</param>
    /// <param name="condition">Required externally observable state.</param>
    private static async Task<CollectorSnapshotDto> UntilAsync(HttpClient client, Func<CollectorSnapshotDto, bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            var snapshot = (await client.GetFromJsonAsync<CollectorSnapshotDto>("/api/collection", deadline.Token))!;
            if (condition(snapshot))
            {
                return snapshot;
            }
            await Task.Delay(100, deadline.Token);
        }
    }
}
