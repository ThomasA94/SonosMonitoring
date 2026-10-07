using System.Text;
using SonosMonitoring.Application.Collection;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Builds bounded, readable metric metadata without changing measurement identities.</summary>
internal static class CollectorDeviceMetadata
{
    /// <summary>Limits each metadata value to a bounded number of Unicode scalars.</summary>
    private const int MAX_LABEL_RUNES = 128;

    /// <summary>Resolves room names, model fallbacks and duplicate names for the current inventory.</summary>
    /// <param name="devices">Known players whose stable identifiers remain the measurement keys.</param>
    /// <returns>Readable labels keyed by the original player identifier.</returns>
    internal static IReadOnlyDictionary<string, string> Names(IReadOnlyList<DeviceSnapshot> devices)
    {
        Dictionary<string, string> names = devices.ToDictionary(device => device.DeviceId, BaseName, StringComparer.Ordinal);
        HashSet<string> duplicates = names.Values.GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (DeviceSnapshot device in devices)
        {
            string name = names[device.DeviceId];
            if (duplicates.Contains(name))
            {
                string prefix = string.Concat(name.EnumerateRunes().Take(MAX_LABEL_RUNES - 11).Select(rune => rune.ToString()));
                names[device.DeviceId] = $"{prefix} ({CollectorMetricsFormatter.DeviceLabel(device.DeviceId)[..8]})";
            }
        }
        return names;
    }

    /// <summary>Removes control characters and bounds metadata supplied by a player.</summary>
    /// <param name="value">Optional untrusted device metadata.</param>
    /// <returns>Printable bounded text, or empty when no text was reported.</returns>
    internal static string Text(string? value) => value is null ? string.Empty :
        string.Concat(value.EnumerateRunes().Where(rune => Rune.IsControl(rune) == false)
            .Take(MAX_LABEL_RUNES).Select(rune => rune.ToString())).Trim();

    /// <summary>Prefers the reported room name, then model, then a readable player fallback.</summary>
    /// <param name="device">Known player.</param>
    /// <returns>A readable base name independent of the measurement key.</returns>
    private static string BaseName(DeviceSnapshot device)
    {
        string room = Text(device.RoomName);
        if (room.Length > 0)
        {
            return room;
        }
        string model = Text(device.ModelName);
        return model.Length > 0 ? model : "Sonos player";
    }
}
