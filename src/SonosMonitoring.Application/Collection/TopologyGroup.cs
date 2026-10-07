namespace SonosMonitoring.Application.Collection;

/// <summary>A playback group observed by one player, without inferred physical network paths.</summary>
/// <param name="GroupId">Opaque group identifier, which can change on regrouping.</param>
/// <param name="CoordinatorDeviceId">Stable UDN of the reported coordinator.</param>
/// <param name="Members">Physical members including explicitly nested satellites.</param>
public sealed record TopologyGroup(string GroupId, string CoordinatorDeviceId, IReadOnlyList<TopologyMember> Members);
