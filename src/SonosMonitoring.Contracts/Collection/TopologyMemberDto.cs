namespace SonosMonitoring.Contracts.Collection;

/// <summary>A topology reference that does not assert direct reachability or a speaker role.</summary>
/// <param name="DeviceId">Stable uppercase UDN.</param>
/// <param name="RoomName">Reported zone name, if supplied.</param>
/// <param name="IsInvisible">Reported visibility flag, if supplied.</param>
/// <param name="ParentDeviceId">Enclosing member for an explicitly nested satellite.</param>
public sealed record TopologyMemberDto(string DeviceId, string? RoomName, bool? IsInvisible, string? ParentDeviceId)
{
    /// <summary>Gets the reported canonical address; an out-of-scope reference is never automatically contacted.</summary>
    public string? ReportedAddress { get; init; }
}
