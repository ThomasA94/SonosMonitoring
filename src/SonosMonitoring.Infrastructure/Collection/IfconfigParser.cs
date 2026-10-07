using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using SonosMonitoring.Application.Collection;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Extracts raw interface counters from the device's diagnostic ifconfig text.</summary>
public static partial class IfconfigParser
{
    /// <summary>Parses interface blocks without inferring traffic rates or radio quality.</summary>
    /// <param name="response">Bounded diagnostic response.</param>
    /// <returns>Recognized counters, excluding incomplete interfaces.</returns>
    public static IReadOnlyList<InterfaceCounters> Parse(string response)
    {
        string plain = WebUtility.HtmlDecode(Regex.Replace(response, "<[^>]*>", string.Empty,
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
        List<InterfaceCounters> result = [];
        string? name = null;
        List<string> block = [];
        foreach (string line in plain.Split('\n'))
        {
            Match header = Header().Match(line.TrimEnd('\r'));
            if (header.Success)
            {
                AddBlock(name, block, result);
                name = header.Groups["name"].Value;
                block = [line];
            }
            else if (name is not null)
            {
                block.Add(line);
            }
        }

        AddBlock(name, block, result);
        return result;
    }

    /// <summary>Adds a complete interface block with all required counters.</summary>
    /// <param name="name">Validated header name.</param>
    /// <param name="lines">Block lines.</param>
    /// <param name="result">Destination list.</param>
    private static void AddBlock(string? name, List<string> lines, List<InterfaceCounters> result)
    {
        if (name is null)
        {
            return;
        }

        string value = string.Join(' ', lines);
        Match rx = ReceivePackets().Match(value);
        Match tx = TransmitPackets().Match(value);
        Match bytes = Bytes().Match(value);
        if (rx.Success == false || tx.Success == false || bytes.Success == false)
        {
            return;
        }

        static ulong Read(Group group) => ulong.Parse(group.Value, CultureInfo.InvariantCulture);
        result.Add(new InterfaceCounters(name,
            Read(bytes.Groups["rx"]), Read(bytes.Groups["tx"]),
            Read(rx.Groups["packets"]), Read(tx.Groups["packets"]),
            Read(rx.Groups["errors"]), Read(tx.Groups["errors"]),
            Read(rx.Groups["dropped"]), Read(tx.Groups["dropped"])));
    }

    /// <summary>Matches the start of a conventional ifconfig block.</summary>
    /// <returns>A compiled header matcher.</returns>
    [GeneratedRegex(@"^(?<name>[A-Za-z][A-Za-z0-9_.-]{0,15})\s+(?:Link encap:|flags=|[0-9]+:)", RegexOptions.CultureInvariant, 100)]
    private static partial Regex Header();

    /// <summary>Matches receive packet, error, and drop counters.</summary>
    /// <returns>A compiled counter matcher.</returns>
    [GeneratedRegex(@"RX packets[: ]+(?<packets>\d+)\s+errors[: ]+(?<errors>\d+)\s+dropped[: ]+(?<dropped>\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 100)]
    private static partial Regex ReceivePackets();

    /// <summary>Matches transmit packet, error, and drop counters.</summary>
    /// <returns>A compiled counter matcher.</returns>
    [GeneratedRegex(@"TX packets[: ]+(?<packets>\d+)\s+errors[: ]+(?<errors>\d+)\s+dropped[: ]+(?<dropped>\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 100)]
    private static partial Regex TransmitPackets();

    /// <summary>Matches receive and transmit bytes in their conventional order.</summary>
    /// <returns>A compiled byte matcher.</returns>
    [GeneratedRegex(@"RX bytes[: ]+(?<rx>\d+).*?TX bytes[: ]+(?<tx>\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 100)]
    private static partial Regex Bytes();
}
