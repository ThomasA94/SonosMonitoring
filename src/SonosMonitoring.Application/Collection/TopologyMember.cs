namespace SonosMonitoring.Application.Collection;

/// <summary>A reported physical group member; its presence does not prove direct reachability.</summary>
/// <param name="DeviceId">Stable uppercase UDN.</param>
/// <param name="RoomName">Optional reported zone name.</param>
/// <param name="IsInvisible">Reported visibility flag, or null when absent.</param>
/// <param name="ParentDeviceId">Enclosing member for a satellite; null for a direct member.</param>
public sealed record TopologyMember(string DeviceId, string? RoomName, bool? IsInvisible, string? ParentDeviceId)
{
    /// <summary>Gets the canonical address advertised by the reporter, without asserting identity or reachability.</summary>
    public string? ReportedAddress { get; init; }
}
