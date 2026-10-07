namespace SonosMonitoring.Contracts.Collection;

/// <summary>Transport view of a reported playback group, not a physical network connection.</summary>
/// <param name="GroupId">Opaque group identifier.</param>
/// <param name="CoordinatorDeviceId">Reported coordinator UDN.</param>
/// <param name="Members">Reported physical members and satellites.</param>
public sealed record TopologyGroupDto(string GroupId, string CoordinatorDeviceId, IReadOnlyList<TopologyMemberDto> Members);
