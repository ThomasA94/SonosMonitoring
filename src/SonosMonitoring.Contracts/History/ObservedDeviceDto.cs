namespace SonosMonitoring.Contracts.History;

/// <summary>Historical source evidence without a claim about physical device or audio health.</summary>
/// <param name="DeviceId">Validated reporter identity.</param>
/// <param name="Address">Last validated address.</param>
/// <param name="DescriptionState">Available, failed, stale, or unobserved description quality.</param>
/// <param name="TopologyState">Quality of the retained topology.</param>
/// <param name="Groups">Reporter-specific group memberships.</param>
public sealed record ObservedDeviceDto(string DeviceId, string Address, string DescriptionState, string TopologyState,
    IReadOnlyList<ObservedGroupDto> Groups);
