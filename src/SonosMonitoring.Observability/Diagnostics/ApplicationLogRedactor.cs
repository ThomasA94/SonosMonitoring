using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace SonosMonitoring.Observability.Diagnostics;

/// <summary>Removes configured secrets and common credential forms before any diagnostic persistence.</summary>
internal sealed class ApplicationLogRedactor
{
    private static readonly Regex _credentials = new(
        "(?i)(password|pwd|token|secret|api[-_]?key|access[-_]?key|authorization|cookie|credential|connectionstring|connection string)[\"']?\\s*[=:]\\s*(?:\"[^\"]*\"|'[^']*'|[^\\s;,]+)",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex _authorization = new("(?i)\\b(Bearer|Basic)\\s+[^\\s,;]+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex _headers = new("(?im)\\b(Authorization|Proxy-Authorization|Cookie|Set-Cookie|X-Api-Key)\\s*:\\s*[^\\r\\n]+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex _uriCredentials = new("(?i)([a-z][a-z0-9+.-]*://)[^\\s/@]+@", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex _query = new("([?&])[^\\s#]+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex _stackFrames = new("(?m)^\\s*(?:at |--- End of ).*(?:\\r?\\n|$)", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private readonly string[] _secrets;

    /// <summary>Captures sensitive configuration values without exposing them through diagnostics.</summary>
    /// <param name="configuration">Effective application configuration.</param>
    public ApplicationLogRedactor(IConfiguration configuration)
    {
        this._secrets = configuration.AsEnumerable()
            .Where(pair => string.IsNullOrEmpty(pair.Value) == false && IsSensitiveKey(pair.Key))
            .Select(pair => pair.Value!).Distinct(StringComparer.Ordinal).OrderByDescending(value => value.Length).ToArray();
    }

    /// <summary>Sanitizes a message and bounds its persisted length; failed sanitization discards the message.</summary>
    /// <param name="value">Untrusted logger message or category.</param>
    /// <param name="limit">Maximum output character count.</param>
    /// <returns>Safe, bounded text.</returns>
    public string Sanitize(string value, int limit)
    {
        if (value.Length > 131072)
        {
            return "[Oversized diagnostic message omitted]";
        }

        try
        {
            foreach (var secret in this._secrets)
            {
                value = value.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
            }

            value = _headers.Replace(value, "$1: [REDACTED]");
            value = _authorization.Replace(value, "$1 [REDACTED]");
            value = _credentials.Replace(value, "$1=[REDACTED]");
            value = _uriCredentials.Replace(value, "$1[REDACTED]@");
            value = _query.Replace(value, "$1[REDACTED]");
            value = _stackFrames.Replace(value, string.Empty);
            return value.Length <= limit ? value : string.Concat(value.AsSpan(0, limit - 1), "…");
        }
        catch (RegexMatchTimeoutException)
        {
            return "[Diagnostic message omitted because sanitization timed out]";
        }
    }

    /// <summary>Identifies configuration keys that can contain credentials.</summary>
    /// <param name="key">Hierarchical configuration key.</param>
    /// <returns>Whether the value must be removed from diagnostics.</returns>
    private static bool IsSensitiveKey(string key)
    {
        return new[] { "password", "pwd", "token", "secret", "key", "connectionstring", "credential", "authorization", "cookie" }
            .Any(part => key.Contains(part, StringComparison.OrdinalIgnoreCase));
    }
}
