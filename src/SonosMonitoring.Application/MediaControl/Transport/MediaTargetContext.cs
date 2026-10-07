namespace SonosMonitoring.Application.MediaControl.Transport;

/// <summary>Binds a request to a selected identity, household, group, source, and optional queue revision.</summary>
/// <param name="DeviceKey">Selected stable UDN.</param>
/// <param name="HouseholdId">Validated Sonos household.</param>
/// <param name="CoordinatorDeviceKey">Displayed coordinator UDN.</param>
/// <param name="MemberDeviceKeys">Displayed physical group UDNs.</param>
/// <param name="SourceToken">Opaque fingerprint of the current media source.</param>
/// <param name="QueueRevision">Optional queue revision.</param>
public sealed record MediaTargetContext(string DeviceKey, string HouseholdId, string CoordinatorDeviceKey,
    IReadOnlyList<string> MemberDeviceKeys, string SourceToken, string? QueueRevision = null);
