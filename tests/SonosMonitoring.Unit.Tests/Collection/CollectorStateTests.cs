using SonosMonitoring.Application.Collection;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Verifies independent source quality and retained last good values.</summary>
public sealed class CollectorStateTests
{
    /// <summary>Keeps prior counters after a source failure and marks old observations stale.</summary>
    [Fact]
    public void RetainsLastGoodCountersAndMarksStale()
    {
        CollectorState state = new(new CollectorOptions { Enabled = true, StaleAfter = TimeSpan.FromSeconds(30) });
        DateTimeOffset old = DateTimeOffset.UtcNow.AddMinutes(-2);
        state.DescriptionSucceeded(new PlayerDescription("RINCON_1", "Office", "Test"), "192.168.6.2", old);
        state.InterfaceSucceeded("RINCON_1", [new InterfaceCounters("br0", 10, 20, 1, 2, 0, 0, 0, 0)], old);
        state.InterfaceFailed("RINCON_1", DateTimeOffset.UtcNow, "timeout");

        DeviceSnapshot device = Assert.Single(state.GetSnapshot().Devices);
        Assert.Equal(10UL, Assert.Single(device.Interfaces).ReceiveBytes);
        Assert.False(device.InterfaceStatus.LastAttemptSucceeded);
        Assert.True(device.InterfaceStatus.IsStale);
        Assert.Equal("timeout", device.InterfaceStatus.ErrorCode);
        Assert.True(device.Description.IsStale);
    }

    /// <summary>Retains source provenance and protects nested cached collections from caller mutations.</summary>
    [Fact]
    public void RetainsTopologyAfterFailureAndReplacesCompleteGroupsOnRecovery()
    {
        CollectorState state = new(new CollectorOptions { StaleAfter = TimeSpan.FromSeconds(30) });
        DateTimeOffset old = DateTimeOffset.UtcNow.AddMinutes(-2);
        state.DescriptionSucceeded(new PlayerDescription("RINCON_1", null, null), "192.168.6.2", old);
        TopologyMember[] members = [new("RINCON_1", "Office", false, null)];
        state.TopologySucceeded("RINCON_1", [new TopologyGroup("first", "RINCON_1", members)], old);
        members[0] = new TopologyMember("RINCON_OTHER", null, null, null);
        state.TopologyFailed("RINCON_1", DateTimeOffset.UtcNow, "invalid_response");
        DeviceSnapshot failed = Assert.Single(state.GetSnapshot().Devices);
        Assert.Equal(old, failed.TopologyStatus.LastSuccessUtc);
        Assert.False(failed.TopologyStatus.LastAttemptSucceeded);
        Assert.True(failed.TopologyStatus.IsStale);
        Assert.Equal("RINCON_1", Assert.Single(Assert.Single(failed.Groups).Members).DeviceId);
        ((TopologyMember[])failed.Groups[0].Members)[0] = members[0];
        Assert.Equal("RINCON_1", state.GetSnapshot().Devices[0].Groups[0].Members[0].DeviceId);

        state.TopologySucceeded("RINCON_1", [new TopologyGroup("replacement", "RINCON_1", [])], DateTimeOffset.UtcNow);
        DeviceSnapshot recovered = Assert.Single(state.GetSnapshot().Devices);
        Assert.Equal("replacement", Assert.Single(recovered.Groups).GroupId);
        Assert.True(recovered.TopologyStatus.LastAttemptSucceeded);
        Assert.Null(recovered.TopologyStatus.ErrorCode);
        Assert.False(recovered.TopologyStatus.IsStale);
    }

    /// <summary>Uses UDN as identity when the same player moves to another address.</summary>
    [Fact]
    public void ReusesIdentityAfterAddressChange()
    {
        CollectorState state = new(new CollectorOptions());
        PlayerDescription description = new("RINCON_1", "Office", "Test");
        state.DescriptionSucceeded(description, "192.168.6.2", DateTimeOffset.UtcNow.AddMinutes(-1));
        state.DescriptionSucceeded(description, "192.168.6.3", DateTimeOffset.UtcNow);

        Assert.Equal("192.168.6.3", Assert.Single(state.GetSnapshot().Devices).Address);
    }

    /// <summary>Signals a full live cache without silently admitting unbounded new identities.</summary>
    [Fact]
    public void SignalsCapacityExceeded()
    {
        CollectorState state = new(new CollectorOptions { MaxKnownDevices = 1 });
        Assert.True(state.DescriptionSucceeded(new PlayerDescription("RINCON_1", null, null),
            "192.168.6.1", DateTimeOffset.UtcNow));
        Assert.False(state.DescriptionSucceeded(new PlayerDescription("RINCON_2", null, null),
            "192.168.6.2", DateTimeOffset.UtcNow));

        CollectorSnapshot snapshot = state.GetSnapshot();
        Assert.True(snapshot.CapacityExceeded);
        Assert.Single(snapshot.Devices);
    }
}
