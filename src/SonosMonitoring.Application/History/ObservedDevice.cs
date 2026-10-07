namespace SonosMonitoring.Application.History;

/// <summary>Comparable observation evidence, excluding counters and volatile polling timestamps.</summary>
/// <param name="DeviceId">Reporter identity validated by its device description.</param>
/// <param name="Address">Last validated address.</param>
/// <param name="DescriptionState">Available, failed, or stale source evidence; not an audio-health verdict.</param>
/// <param name="TopologyState">Quality of the retained topology.</param>
/// <param name="Groups">Canonical reporter-specific grouping, without inferred physical roles.</param>
public sealed record ObservedDevice(string DeviceId, string Address, string DescriptionState, string TopologyState,
    IReadOnlyList<ObservedGroup> Groups);
