using System.Globalization;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Validated identity and sequence metadata from a UPnP GENA notification.</summary>
/// <param name="Sid">The subscription identifier assigned by the publisher.</param>
/// <param name="Sequence">The unsigned sequence number from the notification.</param>
public sealed record GenaHeaders(string Sid, uint Sequence)
{
    private const string NOTIFICATION_TYPE = "upnp:event";
    private const string NOTIFICATION_SUBTYPE = "upnp:propchange";

    /// <summary>Validates and parses the required NOTIFY headers.</summary>
    /// <param name="sid">The subscription identifier header.</param>
    /// <param name="nt">The notification type header.</param>
    /// <param name="nts">The notification subtype header.</param>
    /// <param name="sequence">The decimal event sequence header.</param>
    /// <returns>The validated SID and sequence.</returns>
    /// <exception cref="FormatException">A required header is missing or invalid.</exception>
    public static GenaHeaders Parse(string? sid, string? nt, string? nts, string? sequence)
    {
        string validSid = ValidateSid(sid);
        if (nt != NOTIFICATION_TYPE || nts != NOTIFICATION_SUBTYPE)
        {
            throw new FormatException("The GENA notification type headers are invalid.");
        }

        if (string.IsNullOrEmpty(sequence) || sequence.Any(character => character is < '0' or > '9') ||
            uint.TryParse(sequence, NumberStyles.None, CultureInfo.InvariantCulture, out uint parsedSequence) == false)
        {
            throw new FormatException("The GENA sequence header is invalid.");
        }

        return new GenaHeaders(validSid, parsedSequence);
    }

    /// <summary>Checks whether a sequence follows a previously accepted noninitial sequence.</summary>
    /// <param name="previous">The previously accepted noninitial sequence.</param>
    /// <param name="next">The incoming sequence.</param>
    /// <returns><see langword="true"/> when the sequence increments, including uint rollover.</returns>
    public static bool IsSuccessor(uint previous, uint next)
    {
        return previous != 0 && (previous == uint.MaxValue ? next == 1 : next == previous + 1);
    }

    /// <summary>Validates the UPnP subscription identifier character and length constraints.</summary>
    /// <param name="sid">The SID value to validate.</param>
    /// <returns>The validated SID.</returns>
    /// <exception cref="FormatException">The SID is missing or outside the supported format.</exception>
    internal static string ValidateSid(string? sid)
    {
        if (string.IsNullOrEmpty(sid) || sid.Length > 256 || sid.StartsWith("uuid:", StringComparison.Ordinal) == false ||
            sid.Length == 5 || sid.Any(character => character is < (char)0x21 or > (char)0x7e))
        {
            throw new FormatException("The GENA subscription identifier is invalid.");
        }

        return sid;
    }
}
