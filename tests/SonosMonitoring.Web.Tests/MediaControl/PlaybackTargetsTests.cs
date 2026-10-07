using SonosMonitoring.Contracts.Collection;
using SonosMonitoring.Web.MediaControl;

namespace SonosMonitoring.Web.Tests.MediaControl;

/// <summary>Checks room suggestions without inventing satellites or merging reporter claims.</summary>
public sealed class PlaybackTargetsTests
{
    /// <summary>Only current standalone rooms outside the selected group are candidates.</summary>
    [Fact]
    public void Additions_exclude_stale_grouped_hidden_and_selected_players()
    {
        var room = Room("A");
        var stale = Room("B") with { Description = room.Description with { IsStale = true } };
        var grouped = Room("C") with { Groups = [new("g", "C", [new("C", "C", false, null), new("D", "D", false, null)])] };
        var hidden = Room("E") with { Groups = [new("e", "E", [new("E", "E", true, "C")])] };
        Assert.Equal("A", Assert.Single(PlaybackTargets.Additions([room, stale, grouped, hidden, Room("F")], ["F"])).DeviceId);
        Assert.DoesNotContain(hidden, PlaybackTargets.Rooms([room, hidden]));
    }

    /// <summary>Conflicting own reports stay unknown even when another reporter claims a valid group.</summary>
    [Fact]
    public void Ambiguous_topology_is_not_repaired_from_another_reporter()
    {
        var room = Room("A");
        var ambiguous = room with { Groups = [.. room.Groups, new("other", "A", [new("A", "A", false, null)])] };
        var other = Room("B") with { Groups = [.. room.Groups, .. Room("B").Groups] };
        Assert.Null(PlaybackTargets.OwnGroup(ambiguous));
        Assert.Equal("B", Assert.Single(PlaybackTargets.Rooms([ambiguous, other])).DeviceId);
    }

    /// <summary>Creates a visible standalone room with current source observations.</summary>
    /// <param name="key">Stable identity.</param>
    private static DeviceSnapshotDto Room(string key)
    {
        var now = DateTimeOffset.UtcNow;
        var source = new SourceSnapshotDto("description", true, now, now, false, null);
        return new(key, "192.0.2.1", key, "Sonos", now, now, source, source, [])
        {
            TopologyStatus = source,
            Groups = [new(key, key, [new(key, key, false, null)])]
        };
    }
}
