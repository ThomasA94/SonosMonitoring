using System.Net;

namespace SonosMonitoring.E2E.Tests.Collection.Gena;

/// <summary>Exercises the GENA worker and SOAP reconciliation over isolated loopback HTTP listeners.</summary>
public sealed class GenaWorkerJourneyTests
{
    /// <summary>Handles an initial callback racing SUBSCRIBE, renews the granted lease, and polls once per event.</summary>
    [Fact]
    public async Task RacesInitialEventRenewsAndReconcilesTopology()
    {
        await using GenaLoopbackSimulator simulator = new();
        await simulator.StartAsync();
        await using GenaWorkerHarness harness = new();
        await harness.StartAsync();

        await UntilAsync(() => simulator.CreatedLeaseCount == 1, TimeSpan.FromSeconds(5));
        string sid = Assert.Single(simulator.CreatedSids);
        await UntilAsync(() => harness.Device().Gena.InitialNotificationReceived, TimeSpan.FromSeconds(5));
        Assert.Equal(1, simulator.InitialResponseRaceCount);
        Assert.Contains(HttpStatusCode.OK, simulator.NotificationStatuses);
        await UntilAsync(() => simulator.TopologyCount >= 1, TimeSpan.FromSeconds(5));

        int beforeEventPoll = simulator.TopologyCount;
        await Task.Delay(TimeSpan.FromSeconds(1.1));
        Assert.Equal(HttpStatusCode.PreconditionFailed, await simulator.NotifyFromForeignPeerAsync(sid));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, await simulator.NotifyAsync(sid, "1", new string('x', 300000)));
        Assert.Equal(HttpStatusCode.BadRequest,
            await simulator.NotifyAsync(sid, "2", TopologyEventXml(), "wrong:event"));
        Assert.Equal(HttpStatusCode.BadRequest,
            await simulator.NotifyAsync(sid, "2", "<broken>"));
        Assert.Equal(HttpStatusCode.PreconditionFailed,
            await simulator.NotifyAsync("uuid:wrong-lease", "2", TopologyEventXml()));
        Assert.Equal(HttpStatusCode.OK, await simulator.NotifyAsync(sid, "1", TopologyEventXml()));
        Assert.Equal(HttpStatusCode.OK, await simulator.NotifyAsync(sid, "1", TopologyEventXml()));
        await UntilAsync(() => simulator.TopologyCount == beforeEventPoll + 1, TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromSeconds(1.2));
        Assert.Equal(beforeEventPoll + 1, simulator.TopologyCount);

        await UntilAsync(() => simulator.RenewalCount >= 1, TimeSpan.FromSeconds(5));
        Assert.All(simulator.RenewedSids, renewedSid => Assert.Equal(sid, renewedSid));
        Assert.True(simulator.RenewalContractValid);
        Assert.True(harness.Device().Gena.Subscribed);

        await harness.StopAsync();
        await UntilAsync(() => simulator.UnsubscribeCount == 1, TimeSpan.FromSeconds(5));
    }

    /// <summary>Replaces a lease whose initial callback was blocked and accepts the next initial event.</summary>
    [Fact]
    public async Task ReplacesLeaseWhenInitialCallbackDoesNotArrive()
    {
        await using GenaLoopbackSimulator simulator = new();
        simulator.DropNextInitialCallbacks(1);
        await simulator.StartAsync();
        await using GenaWorkerHarness harness = new(TimeSpan.FromSeconds(1));
        await harness.StartAsync();

        await UntilAsync(() => simulator.CreatedLeaseCount == 1 && harness.Device().Gena.Subscribed, TimeSpan.FromSeconds(5));
        string firstSid = Assert.Single(simulator.CreatedSids);
        await UntilAsync(() => simulator.CreatedLeaseCount >= 2, TimeSpan.FromSeconds(12));
        await UntilAsync(() => harness.Device().Gena.InitialNotificationReceived, TimeSpan.FromSeconds(5));

        string[] sids = simulator.CreatedSids.ToArray();
        Assert.Equal(2, sids.Length);
        Assert.NotEqual(firstSid, sids[1]);
        Assert.True(simulator.InitialResponseRaceCount >= 1);
        await UntilAsync(() => simulator.TopologyCount >= 1, TimeSpan.FromSeconds(5));

        await harness.StopAsync();
        await UntilAsync(() => simulator.UnsubscribeCount >= 1, TimeSpan.FromSeconds(5));
    }

    /// <summary>Replaces a publisher-rejected renewal with a distinct confirmed lease.</summary>
    [Fact]
    public async Task RecreatesLeaseAfterPublisherRejectsRenewal()
    {
        await using GenaLoopbackSimulator simulator = new();
        await simulator.StartAsync();
        await using GenaWorkerHarness harness = new();
        await harness.StartAsync();
        await UntilAsync(() => harness.Device().Gena.InitialNotificationReceived, TimeSpan.FromSeconds(5));
        string first = Assert.Single(simulator.CreatedSids);
        simulator.RejectNextRenewal();
        await UntilAsync(() => simulator.CreatedLeaseCount == 2 && harness.Device().Gena.InitialNotificationReceived, TimeSpan.FromSeconds(10));
        Assert.NotEqual(first, simulator.CreatedSids[1]);
        await harness.StopAsync();
        Assert.Equal(2, simulator.UnsubscribeCount);
    }

    /// <summary>Waits in short intervals for an observable simulator or worker condition.</summary>
    /// <param name="condition">Condition that must eventually become true.</param>
    /// <param name="timeout">Maximum wait duration.</param>
    private static async Task UntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using CancellationTokenSource deadline = new(timeout);
        while (condition() == false)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), deadline.Token);
        }
    }

    /// <summary>Builds the minimal valid GENA topology hint used by the callback checks.</summary>
    /// <returns>Valid UPnP event property set XML.</returns>
    private static string TopologyEventXml() =>
        "<e:propertyset xmlns:e=\"urn:schemas-upnp-org:event-1-0\"><e:property><ZoneGroupState>hint</ZoneGroupState></e:property></e:propertyset>";
}
