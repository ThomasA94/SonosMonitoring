using SonosMonitoring.Application.Collection;
using SonosMonitoring.Application.MediaControl.Transport;
using SonosMonitoring.Infrastructure.Collection;
using SonosMonitoring.Infrastructure.MediaControl.Transport;

namespace SonosMonitoring.Unit.Tests.MediaControl.Transport;

/// <summary>Checks real gateway grouping semantics against multiple isolated SOAP players.</summary>
public sealed class GroupManagementTests
{
    /// <summary>Leave availability comes from the member that receives the write, not the coordinator.</summary>
    /// <param name="unsupportedAddress">Player without the standalone action.</param>
    /// <param name="expected">Whether B can leave.</param>
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.0.0.2", false)]
    public async Task Leave_uses_the_members_own_capability(string unsupportedAddress, bool expected)
    {
        using SimulatedMediaPlayer handler = CreatePlayers(true);
        handler.OmittedActionsByAddress[unsupportedAddress] = "BecomeCoordinatorOfStandaloneGroup";
        using MediaDeviceGateway gateway = CreateGateway(handler);
        var member = await gateway.ReadAsync("RINCON_B", CancellationToken.None);
        Assert.Equal(expected, member.AvailableActions.Contains("leave"));
    }

    /// <summary>Joining writes only the added room; leaving writes only the removed member.</summary>
    [Fact]
    public async Task Join_and_leave_address_the_member_and_confirm_observed_membership()
    {
        using SimulatedMediaPlayer handler = CreatePlayers(false);
        using MediaDeviceGateway gateway = CreateGateway(handler);
        var source = await gateway.ReadAsync("RINCON_B", CancellationToken.None);
        var destination = await gateway.ReadAsync("RINCON_A", CancellationToken.None);
        var inputs = new MediaWriteInputs(Reference: "RINCON_A", DestinationContext: destination.Context);
        var join = Assert.Single(await gateway.PrepareAsync("RINCON_B", "join", source.Context, inputs, CancellationToken.None));
        Assert.Equal("x-rincon:RINCON_A", join.Arguments["CurrentURI"]);
        Assert.Empty(handler.WriteTargets);
        await gateway.SendAsync(join, CancellationToken.None);
        Assert.Equal(("127.0.0.2", "SetAVTransportURI"), Assert.Single(handler.WriteTargets));
        handler.TopologyXml = Topology(true);
        Assert.True(await gateway.ConfirmAsync("join", source.Context, inputs, CancellationToken.None));
        var member = await gateway.ReadAsync("RINCON_B", CancellationToken.None);
        var leave = Assert.Single(await gateway.PrepareAsync("RINCON_B", "leave", member.Context, new(), CancellationToken.None));
        await gateway.SendAsync(leave, CancellationToken.None);
        Assert.Equal(("127.0.0.2", "BecomeCoordinatorOfStandaloneGroup"), handler.WriteTargets[1]);
        handler.TopologyXml = Topology(false);
        Assert.True(await gateway.ConfirmAsync("leave", member.Context, new(), CancellationToken.None));
    }

    /// <summary>A changed displayed destination cannot be silently replaced by a new group context.</summary>
    [Fact]
    public async Task Changed_destination_is_rejected_before_any_write()
    {
        using SimulatedMediaPlayer handler = CreatePlayers(false);
        using MediaDeviceGateway gateway = CreateGateway(handler);
        var source = await gateway.ReadAsync("RINCON_B", CancellationToken.None);
        var destination = await gateway.ReadAsync("RINCON_A", CancellationToken.None);
        var error = await Assert.ThrowsAsync<MediaDeviceException>(() => gateway.PrepareAsync("RINCON_B", "join", source.Context,
            new(Reference: "RINCON_A", DestinationContext: destination.Context with { SourceToken = "outdated" }), CancellationToken.None));
        Assert.Equal("context_changed", error.Code);
        Assert.Empty(handler.WriteTargets);
    }

    /// <summary>Cross-household joins and coordinator removal remain explicitly unsupported.</summary>
    [Fact]
    public async Task Household_boundary_and_coordinator_are_preserved()
    {
        using SimulatedMediaPlayer handler = CreatePlayers(false);
        using MediaDeviceGateway gateway = CreateGateway(handler);
        handler.HouseholdsByAddress["127.0.0.2"] = "HH_2";
        var source = await gateway.ReadAsync("RINCON_B", CancellationToken.None);
        var destination = await gateway.ReadAsync("RINCON_A", CancellationToken.None);
        var error = await Assert.ThrowsAsync<MediaDeviceException>(() => gateway.PrepareAsync("RINCON_B", "join", source.Context,
            new(Reference: "RINCON_A", DestinationContext: destination.Context), CancellationToken.None));
        Assert.Equal("invalid_group_target", error.Code);
        handler.HouseholdsByAddress.Clear();
        handler.TopologyXml = Topology(true);
        var coordinator = await gateway.ReadAsync("RINCON_A", CancellationToken.None);
        Assert.DoesNotContain("leave", coordinator.AvailableActions);
        await Assert.ThrowsAsync<MediaDeviceException>(() => gateway.PrepareAsync("RINCON_A", "leave", coordinator.Context, new(), CancellationToken.None));
        Assert.Empty(handler.WriteTargets);
    }

    /// <summary>Transport controls selected through a member still address the playback coordinator.</summary>
    [Fact]
    public async Task Playback_targets_the_group_coordinator()
    {
        using SimulatedMediaPlayer handler = CreatePlayers(true);
        using MediaDeviceGateway gateway = CreateGateway(handler);
        var member = await gateway.ReadAsync("RINCON_B", CancellationToken.None);
        var play = Assert.Single(await gateway.PrepareAsync("RINCON_B", "resume", member.Context, new(), CancellationToken.None));
        await gateway.SendAsync(play, CancellationToken.None);
        Assert.Equal(("127.0.0.1", "Play"), Assert.Single(handler.WriteTargets));
    }

    /// <summary>Creates fixed local identities with configurable native grouping.</summary>
    /// <param name="grouped">Whether both rooms share a coordinator.</param>
    private static SimulatedMediaPlayer CreatePlayers(bool grouped)
    {
        var handler = new SimulatedMediaPlayer { TopologyXml = Topology(grouped) };
        handler.DeviceKeysByAddress.Add("127.0.0.1", "RINCON_A");
        handler.DeviceKeysByAddress.Add("127.0.0.2", "RINCON_B");
        return handler;
    }

    /// <summary>Builds a two-room UPnP topology.</summary>
    /// <param name="grouped">Whether B belongs to A.</param>
    private static string Topology(bool grouped) => "<ZoneGroups><ZoneGroup ID=\"A:1\" Coordinator=\"RINCON_A\"><ZoneGroupMember UUID=\"RINCON_A\" />" +
        (grouped ? "" : "</ZoneGroup><ZoneGroup ID=\"B:1\" Coordinator=\"RINCON_B\">") +
        "<ZoneGroupMember UUID=\"RINCON_B\" /></ZoneGroup></ZoneGroups>";

    /// <summary>Creates an isolated collector view whose SOAP calls never leave the fake handler.</summary>
    /// <param name="handler">Simulated household.</param>
    private static MediaDeviceGateway CreateGateway(SimulatedMediaPlayer handler)
    {
        var now = DateTimeOffset.UtcNow;
        var quality = new SourceSnapshot("description", true, now, now, false, null);
        var devices = handler.DeviceKeysByAddress.Select(pair => new DeviceSnapshot(pair.Value, pair.Key, pair.Value, "Sonos",
            now, now, quality, quality, [])
        { TopologyStatus = quality }).ToArray();
        var snapshot = new CollectorSnapshot(true, now, now, devices, [], false) { HeartbeatMaxAgeSeconds = 30 };
        return new(new MediaFixedSnapshotReader(snapshot), new CollectorOptions
        {
            Enabled = true,
            AllowedCidrs = ["127.0.0.0/8"],
            AllowLoopback = true,
            RequestTimeout = TimeSpan.FromSeconds(1),
            StaleAfter = TimeSpan.FromMinutes(1)
        }, handler);
    }
}
