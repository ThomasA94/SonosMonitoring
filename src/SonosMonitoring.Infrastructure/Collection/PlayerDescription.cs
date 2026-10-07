namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Validated identity and display metadata from a Sonos device description.</summary>
/// <param name="DeviceId">Stable uppercase player UDN without uuid:.</param>
/// <param name="RoomName">Reported room name, if available.</param>
/// <param name="ModelName">Reported model name, if available.</param>
public sealed record PlayerDescription(string DeviceId, string? RoomName, string? ModelName)
{
    /// <summary>Gets the reported software version.</summary>
    public string? SoftwareVersion { get; init; }
    /// <summary>Gets the reported hardware version.</summary>
    public string? HardwareVersion { get; init; }
    /// <summary>Gets the reported model number.</summary>
    public string? ModelNumber { get; init; }
    /// <summary>Gets whether the description advertises the supported local transport control path.</summary>
    public bool SupportsPlayback { get; init; }
    /// <summary>Gets whether the description advertises the supported local rendering control path.</summary>
    public bool SupportsVolume { get; init; }

    /// <summary>Gets whether the description advertises the supported local topology event path.</summary>
    public bool SupportsTopologyEvents { get; init; }
}
