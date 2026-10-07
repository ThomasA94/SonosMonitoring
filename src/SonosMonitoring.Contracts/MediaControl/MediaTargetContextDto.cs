namespace SonosMonitoring.Contracts.MediaControl;

/// <summary>Binds an intention to the displayed topology and media source, independently of network addresses.</summary>
/// <param name="DeviceKey">Selected stable player identity.</param>
/// <param name="HouseholdId">Validated household identity.</param>
/// <param name="CoordinatorDeviceKey">Displayed group coordinator.</param>
/// <param name="MemberDeviceKeys">Displayed physical group members.</param>
/// <param name="SourceToken">Opaque fingerprint of the current media source.</param>
/// <param name="QueueRevision">Optional observed queue revision.</param>
public sealed record MediaTargetContextDto(string DeviceKey, string HouseholdId, string CoordinatorDeviceKey,
    IReadOnlyList<string> MemberDeviceKeys, string SourceToken, string? QueueRevision = null);
