using System.Net;
using System.Net.Sockets;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Recognizes the fixed local player-description URL without trusting or following the reference.</summary>
public static class PlayerDescriptionLocation
{
    /// <summary>Extracts only a canonical IPv4 address from the supported HTTP description URL.</summary>
    /// <param name="location">Untrusted advertised URL.</param>
    /// <returns>A literal address, or null for an absent or unsupported URL; callers must enforce their CIDRs.</returns>
    public static string? GetAddress(string? location)
    {
        if (location is null || location.Length > 128 || Uri.TryCreate(location, UriKind.Absolute, out Uri? uri) == false ||
            IPAddress.TryParse(uri.Host, out IPAddress? address) == false || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        string canonical = address.ToString();
        return location == $"http://{canonical}:1400/xml/device_description.xml" ? canonical : null;
    }
}
