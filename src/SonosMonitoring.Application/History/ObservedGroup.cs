namespace SonosMonitoring.Application.History;

/// <summary>Canonical playback-group membership from one reporter.</summary>
/// <param name="GroupId">Reported group identifier.</param>
/// <param name="CoordinatorDeviceId">Reported coordinator identity.</param>
/// <param name="Members">Sorted physical members and explicit parent relationships.</param>
public sealed record ObservedGroup(string GroupId, string CoordinatorDeviceId, IReadOnlyList<ObservedMember> Members);

/// <summary>Explicit membership evidence without inferred stereo or home-theater roles.</summary>
/// <param name="DeviceId">Reported member identity.</param>
/// <param name="ParentDeviceId">Reported satellite parent, if present.</param>
public sealed record ObservedMember(string DeviceId, string? ParentDeviceId);
