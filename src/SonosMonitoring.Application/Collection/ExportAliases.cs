using System.Globalization;

namespace SonosMonitoring.Application.Collection;

/// <summary>Assigns opaque sequential labels within one export, without retaining an identity map afterwards.</summary>
internal sealed class ExportAliases
{
    private readonly Dictionary<(string Kind, string Value), string> _values = [];
    private readonly Dictionary<string, int> _counts = [];

    /// <summary>Reuses a label only for the same value and category in this export.</summary>
    /// <param name="kind">Non-sensitive category prefix.</param>
    /// <param name="value">Original value, never included in the label.</param>
    public string Get(string kind, string value)
    {
        if (this._values.TryGetValue((kind, value), out var alias))
        {
            return alias;
        }
        int next = this._counts.GetValueOrDefault(kind) + 1;
        this._counts[kind] = next;
        alias = kind + "-" + next.ToString(CultureInfo.InvariantCulture);
        this._values.Add((kind, value), alias);
        return alias;
    }

    /// <summary>Preserves absence instead of inventing a value for missing observations.</summary>
    /// <param name="kind">Non-sensitive category prefix.</param>
    /// <param name="value">Optional original value.</param>
    public string? Optional(string kind, string? value) => value is null ? null : this.Get(kind, value);
}
