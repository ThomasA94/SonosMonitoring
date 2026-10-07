namespace SonosMonitoring.Contracts.History;

/// <summary>One historically reported group.</summary>
/// <param name="GroupId">Reported group identifier.</param>
/// <param name="CoordinatorDeviceId">Reported coordinator.</param>
/// <param name="Members">Members with explicit parent relationships.</param>
public sealed record ObservedGroupDto(string GroupId, string CoordinatorDeviceId, IReadOnlyList<ObservedMemberDto> Members);

/// <summary>One historically reported member.</summary>
/// <param name="DeviceId">Member identity.</param>
/// <param name="ParentDeviceId">Explicit satellite parent, if any.</param>
public sealed record ObservedMemberDto(string DeviceId, string? ParentDeviceId);
