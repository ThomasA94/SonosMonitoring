using System.Text.Json;
using SonosMonitoring.Application.Collection;
using SonosMonitoring.Infrastructure.Collection;
using SonosMonitoring.Infrastructure.History;

namespace SonosMonitoring.Unit.Tests.History;

/// <summary>Checks which observation differences carry history meaning.</summary>
public sealed class ObservationNormalizerTests
{
    /// <summary>Polling times, counters, names, and source order do not manufacture grouping changes.</summary>
    [Fact]
    public void EquivalentTopologyHasStableNormalizedState()
    {
        var state = new CollectorState(new());
        var now = DateTimeOffset.UtcNow;
        state.DescriptionSucceeded(new("RINCON_ONE", "Room", "Model"), "192.0.2.1", now);
        TopologyMember first = new("RINCON_ONE", "Room", false, null);
        TopologyMember second = new("RINCON_TWO", "Other", true, "RINCON_ONE");
        state.TopologySucceeded("RINCON_ONE", [new("group", "RINCON_ONE", [second, first])], now);
        var before = ObservationNormalizer.Normalize(Assert.Single(state.GetSnapshot().Devices));
        state.DescriptionSucceeded(new("RINCON_ONE", "New Room", "Model"), "192.0.2.1", now.AddSeconds(2));
        state.TopologySucceeded("RINCON_ONE", [new("group", "RINCON_ONE", [first, second])], now.AddSeconds(2));
        var after = ObservationNormalizer.Normalize(Assert.Single(state.GetSnapshot().Devices));
        Assert.Equal(JsonSerializer.Serialize(before.State), JsonSerializer.Serialize(after.State));
        Assert.NotEqual(before.ObservedAtUtc, after.ObservedAtUtc);
        state.DescriptionFailed("192.0.2.1", now.AddSeconds(3), "timeout");
        var failed = ObservationNormalizer.Normalize(Assert.Single(state.GetSnapshot().Devices));
        Assert.Equal("failed", failed.State.DescriptionState);
        Assert.Equal("available", failed.State.TopologyState);
        Assert.Single(failed.State.Groups);
    }
}
