using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.E2E.Tests.Collection;

/// <summary>Verifies real UDP transport boundaries using ephemeral loopback listeners only.</summary>
public sealed class SsdpSearchTests
{
    /// <summary>Sends one standards-shaped search from the selected address and bounds retained payloads.</summary>
    [Fact]
    public async Task UsesSelectedSourceAndDropsOversizedDatagrams()
    {
        await using SimulatedSsdpResponder responder = new(SimulatedSsdpResponder.Reply(), new string('x', 9000));
        SsdpSearch search = new(responder.Endpoint);
        SsdpSearchResult result = await search.SearchAsync("127.0.0.1", TimeSpan.FromSeconds(2), 8, CancellationToken.None);
        var request = Assert.Single(responder.Requests);
        Assert.Equal("127.0.0.1", request.Source);
        Assert.Equal("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 1\r\nST: " +
            SsdpResponseParser.SEARCH_TARGET + "\r\n\r\n", request.Request);
        Assert.Equal("127.0.0.2", Assert.Single(result.Datagrams).SenderAddress);
        Assert.False(result.LimitReached);
        Assert.Null(result.ErrorCode);
    }

    /// <summary>Counts even invalid and oversized traffic against the receive budget.</summary>
    [Fact]
    public async Task StopsAtDatagramBudget()
    {
        await using SimulatedSsdpResponder responder = new(new string('x', 9000), "garbage", SimulatedSsdpResponder.Reply());
        SsdpSearchResult result = await new SsdpSearch(responder.Endpoint)
            .SearchAsync("127.0.0.1", TimeSpan.FromSeconds(2), 2, CancellationToken.None);
        Assert.True(result.LimitReached);
        Assert.Single(result.Datagrams);
    }

    /// <summary>Distinguishes an ordinary empty response window from host cancellation.</summary>
    [Fact]
    public async Task EmptyWindowCompletesButCancellationPropagates()
    {
        await using SimulatedSsdpResponder responder = new();
        SsdpSearch search = new(responder.Endpoint);
        var result = await search.SearchAsync("127.0.0.1", TimeSpan.FromMilliseconds(100), 8, CancellationToken.None);
        Assert.Empty(result.Datagrams);
        Assert.Null(result.ErrorCode);
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            search.SearchAsync("127.0.0.1", TimeSpan.FromSeconds(5), 8, cancellation.Token));
    }

    /// <summary>A failed interface does not discard replies received by a healthy interface.</summary>
    [Fact]
    public async Task DiscoveryRetainsPartialResultsAndDeduplicatesReplies()
    {
        await using SimulatedSsdpResponder responder = new(SimulatedSsdpResponder.Reply(), SimulatedSsdpResponder.Reply(), SimulatedSsdpResponder.Reply("127.0.0.4"));
        CollectorOptions options = new()
        {
            Enabled = true,
            SsdpEnabled = true,
            SsdpSearchWindow = TimeSpan.FromMilliseconds(300),
            SsdpInterfaceAddresses = ["127.0.0.1", "192.0.2.99"]
        };
        CollectorState state = new(options);
        SsdpDiscovery discovery = new(options, new Ipv4TargetPolicy(["127.0.0.2/32"], true), new SsdpSearch(responder.Endpoint), state);
        Assert.Equal("127.0.0.2", Assert.Single(await discovery.DiscoverAsync(CancellationToken.None)));
        var status = state.GetSnapshot().Ssdp;
        Assert.Equal(1, status.CandidateCount);
        Assert.Equal(1, status.FailedInterfaces);
        Assert.Equal("socket_failure", status.ErrorCode);
        Assert.NotNull(status.LastCompletedUtc);
    }

    /// <summary>Disabled discovery performs no socket operation and cancellation remains visible.</summary>
    [Fact]
    public async Task DisabledDiscoveryIsSilentAndCancellationIsRecorded()
    {
        await using SimulatedSsdpResponder responder = new();
        CollectorOptions options = new() { Enabled = true, SsdpInterfaceAddresses = ["127.0.0.1"] };
        CollectorState state = new(options);
        SsdpDiscovery discovery = new(options, new Ipv4TargetPolicy(["127.0.0.2/32"], true), new SsdpSearch(responder.Endpoint), state);
        Assert.Empty(await discovery.DiscoverAsync(CancellationToken.None));
        Assert.Empty(responder.Requests);
        options.SsdpEnabled = true;
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovery.DiscoverAsync(cancellation.Token));
        Assert.Equal("search_cancelled", state.GetSnapshot().Ssdp.ErrorCode);
        Assert.Null(state.GetSnapshot().Ssdp.LastCompletedUtc);
    }
}
