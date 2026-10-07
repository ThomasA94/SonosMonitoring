using System.Text;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Validates untrusted SSDP replies before they can influence HTTP probe ordering.</summary>
public static class SsdpResponseParser
{
    /// <summary>Gets the Sonos device search target used in requests and replies.</summary>
    public const string SEARCH_TARGET = "urn:schemas-upnp-org:device:ZonePlayer:1";

    /// <summary>Accepts only canonical, approved description locations matching the UDP sender.</summary>
    /// <param name="payload">One complete UDP response, at most 8192 bytes.</param>
    /// <param name="senderAddress">Canonical literal IPv4 sender address.</param>
    /// <param name="policy">Explicit HTTP target allowlist.</param>
    /// <returns>An approved candidate address, or null for any malformed or untrusted response.</returns>
    public static string? Parse(ReadOnlySpan<byte> payload, string senderAddress, Ipv4TargetPolicy policy)
    {
        if (payload.Length > 8192 || policy.IsAllowed(senderAddress) == false)
        {
            return null;
        }

        foreach (byte value in payload)
        {
            if (value > 126 || (value < 32 && value is not (9 or 10 or 13)))
            {
                return null;
            }
        }

        string text = Encoding.ASCII.GetString(payload);
        if (text.EndsWith("\r\n\r\n", StringComparison.Ordinal) == false)
        {
            return null;
        }

        string[] lines = text.Split("\r\n", StringSplitOptions.None);
        if (lines[0].Equals("HTTP/1.1 200 OK", StringComparison.Ordinal) == false)
        {
            return null;
        }

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines.Skip(1).Take(lines.Length - 3))
        {
            int separator = line.IndexOf(':');
            if (separator <= 0 || line.Contains('\r') || line.Contains('\n') ||
                line[..separator].All(IsHeaderTokenCharacter) == false ||
                headers.TryAdd(line[..separator], line[(separator + 1)..].Trim()) == false)
            {
                return null;
            }
        }

        if (headers.GetValueOrDefault("ST") != SEARCH_TARGET ||
            PlayerDescriptionLocation.GetAddress(headers.GetValueOrDefault("LOCATION")) != senderAddress)
        {
            return null;
        }

        string? usn = headers.GetValueOrDefault("USN");
        string suffix = "::" + SEARCH_TARGET;
        if (usn is null || usn.StartsWith("uuid:RINCON_", StringComparison.OrdinalIgnoreCase) == false ||
            usn.EndsWith(suffix, StringComparison.Ordinal) == false)
        {
            return null;
        }

        string id = usn[5..^suffix.Length].ToUpperInvariant();
        return id.Length <= 128 && id.Length > 7 &&
            id.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')
            ? senderAddress : null;
    }

    /// <summary>Accepts only HTTP field-name token characters, including valid extension header names.</summary>
    /// <param name="value">One header-name character.</param>
    /// <returns>Whether the character belongs to the HTTP token alphabet.</returns>
    private static bool IsHeaderTokenCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || "!#$%&'*+-.^_`|~".Contains(value);
}
