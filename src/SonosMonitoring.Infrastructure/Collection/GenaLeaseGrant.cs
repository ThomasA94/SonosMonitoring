using System.Globalization;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>A validated GENA subscription identifier and granted lease duration.</summary>
/// <param name="Sid">The subscription identifier returned by the publisher.</param>
/// <param name="Duration">The finite granted duration, or <see langword="null"/> for infinite.</param>
public sealed record GenaLeaseGrant(string Sid, TimeSpan? Duration)
{
    private const string TIMEOUT_PREFIX = "Second-";

    /// <summary>Parses the granted SID and TIMEOUT response headers.</summary>
    /// <param name="sid">The SID response header.</param>
    /// <param name="timeout">The granted TIMEOUT response header.</param>
    /// <returns>The validated lease grant.</returns>
    /// <exception cref="FormatException">Either response header is malformed.</exception>
    public static GenaLeaseGrant Parse(string? sid, string? timeout)
    {
        string validSid = GenaHeaders.ValidateSid(sid);
        if (timeout is null || timeout.StartsWith(TIMEOUT_PREFIX, StringComparison.OrdinalIgnoreCase) == false)
        {
            throw new FormatException("The GENA timeout header is invalid.");
        }

        string value = timeout[TIMEOUT_PREFIX.Length..];
        if (string.Equals(value, "infinite", StringComparison.OrdinalIgnoreCase))
        {
            return new GenaLeaseGrant(validSid, null);
        }

        if (value.Length == 0 || value.Any(character => character is < '0' or > '9') ||
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) == false || seconds <= 0)
        {
            throw new FormatException("The GENA timeout duration is invalid.");
        }

        return new GenaLeaseGrant(validSid, TimeSpan.FromSeconds(seconds));
    }
}
