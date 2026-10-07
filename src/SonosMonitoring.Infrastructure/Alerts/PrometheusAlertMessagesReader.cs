using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SonosMonitoring.Application.Alerts;

namespace SonosMonitoring.Infrastructure.Alerts;

/// <summary>Reads bounded current alert output and validates the configured SonosMonitoring rule group.</summary>
public sealed class PrometheusAlertMessagesReader : IAlertMessagesReader
{
    private const int MAX_RESPONSE_BYTES = 2 * 1024 * 1024;
    private const int MAX_MESSAGES = 1024;
    private const int MAX_JSON_DEPTH = 32;
    private static readonly TimeSpan _maxEvaluationAge = TimeSpan.FromSeconds(60);
    private const string OBSERVATION_RULE = "sonos:collector_observing";
    private const string FIRING_STARTED_RULE = "sonos:alert_firing_started_timestamp_seconds";
    private static readonly HashSet<string> _expectedAlertRules =
    [
        "SonosApiUnavailable",
        "SonosCollectorInterrupted",
        "SonosDeviceDescriptionUnavailable",
        "SonosApiRecovered",
        "SonosCollectorRecovered",
        "SonosDeviceDescriptionRecovered"
    ];
    private static readonly HashSet<string> _expectedRules = new(_expectedAlertRules, StringComparer.Ordinal)
    {
        OBSERVATION_RULE,
        FIRING_STARTED_RULE
    };
    private static readonly IReadOnlyDictionary<string, string> _recoveryRuleSources =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SonosApiRecovered"] = "SonosApiUnavailable",
            ["SonosCollectorRecovered"] = "SonosCollectorInterrupted",
            ["SonosDeviceDescriptionRecovered"] = "SonosDeviceDescriptionUnavailable"
        };

    private readonly HttpClient _client;

    /// <summary>Receives the configured Prometheus client with its fixed base address.</summary>
    /// <param name="client">HTTP client for the trusted Prometheus service.</param>
    public PrometheusAlertMessagesReader(HttpClient client) => this._client = client;

    /// <inheritdoc />
    public async Task<AlertMessagesSnapshot> ReadAsync(CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(10));
        using HttpResponseMessage response = await this._client.GetAsync("api/v1/rules",
            HttpCompletionOption.ResponseHeadersRead, budget.Token);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(MAX_RESPONSE_BYTES, budget.Token);
        string body = await response.Content.ReadAsStringAsync(budget.Token);
        using JsonDocument document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = MAX_JSON_DEPTH });
        return this.Parse(document.RootElement, DateTimeOffset.UtcNow);
    }

    /// <summary>Checks upstream status, expected rule health, and evaluation freshness before returning messages.</summary>
    /// <param name="root">Parsed Prometheus API response.</param>
    /// <param name="nowUtc">Current UTC time used for the freshness bound.</param>
    /// <returns>A current alert snapshot from one successful rule-group evaluation.</returns>
    private AlertMessagesSnapshot Parse(JsonElement root, DateTimeOffset nowUtc)
    {
        if (this.RequiredString(root, "status") != "success")
        {
            throw this.InvalidResponse();
        }

        JsonElement data = this.RequiredProperty(root, "data", JsonValueKind.Object);
        JsonElement groups = this.RequiredProperty(data, "groups", JsonValueKind.Array);
        JsonElement? expectedGroup = null;
        foreach (JsonElement group in groups.EnumerateArray())
        {
            if (this.RequiredString(group, "name") == "sonosmonitoring")
            {
                if (expectedGroup.HasValue)
                {
                    throw this.InvalidResponse();
                }
                expectedGroup = group;
            }
        }
        if (expectedGroup.HasValue == false)
        {
            throw this.InvalidResponse();
        }

        JsonElement groupElement = expectedGroup.Value;
        DateTimeOffset evaluatedAtUtc = this.ParseTimestamp(this.RequiredString(groupElement, "lastEvaluation"));
        this.ValidateFresh(evaluatedAtUtc, nowUtc);
        double groupDuration = this.RequiredNonNegativeNumber(groupElement, "evaluationTime");
        JsonElement rules = this.RequiredProperty(groupElement, "rules", JsonValueKind.Array);
        HashSet<string> seenRules = new(StringComparer.Ordinal);
        List<AlertMessage> messages = [];
        HashSet<string> activeIdentities = new(StringComparer.Ordinal);
        HashSet<string> recoveredIdentities = new(StringComparer.Ordinal);
        int observedAlertCount = 0;
        foreach (JsonElement rule in rules.EnumerateArray())
        {
            string name = this.RequiredString(rule, "name");
            if (_expectedRules.Contains(name) == false || seenRules.Add(name) == false ||
                this.RequiredString(rule, "health") != "ok")
            {
                throw this.InvalidResponse();
            }

            DateTimeOffset ruleEvaluatedAtUtc = this.ParseTimestamp(this.RequiredString(rule, "lastEvaluation"));
            this.ValidateFresh(ruleEvaluatedAtUtc, nowUtc);
            double ruleDuration = this.RequiredNonNegativeNumber(rule, "evaluationTime");
            string type = this.RequiredString(rule, "type");
            if (name == OBSERVATION_RULE || name == FIRING_STARTED_RULE)
            {
                if (type != "recording")
                {
                    throw this.InvalidResponse();
                }
                continue;
            }
            if (_expectedAlertRules.Contains(name) == false || type != "alerting")
            {
                throw this.InvalidResponse();
            }

            string ruleState = this.RequiredString(rule, "state");
            if (ruleState is not ("inactive" or "pending" or "firing"))
            {
                throw this.InvalidResponse();
            }
            JsonElement alerts = this.RequiredProperty(rule, "alerts", JsonValueKind.Array);
            int alertCount = alerts.GetArrayLength();
            if ((ruleState == "inactive" && alertCount > 0) || (ruleState != "inactive" && alertCount == 0))
            {
                throw this.InvalidResponse();
            }

            foreach (JsonElement alert in alerts.EnumerateArray())
            {
                if (observedAlertCount >= MAX_MESSAGES)
                {
                    throw this.InvalidResponse();
                }
                observedAlertCount++;
                AlertMessage? message = this.ParseAlert(alert, name, evaluatedAtUtc, ruleDuration, nowUtc);
                if (message is not null)
                {
                    messages.Add(message);
                    if (message.State == "recovered")
                    {
                        recoveredIdentities.Add(message.Identity);
                    }
                    else
                    {
                        activeIdentities.Add(message.Identity);
                    }
                }
            }
        }
        if (seenRules.SetEquals(_expectedRules) == false)
        {
            throw this.InvalidResponse();
        }
        if (recoveredIdentities.Overlaps(activeIdentities))
        {
            throw this.InvalidResponse();
        }

        return new AlertMessagesSnapshot(evaluatedAtUtc, groupDuration, messages.ToArray());
    }

    /// <summary>Reduces one active alert or recovery signal to validated, privacy-reduced fields.</summary>
    /// <param name="alert">Prometheus alert instance.</param>
    /// <param name="ruleName">Validated allowlisted rule name.</param>
    /// <param name="evaluatedAtUtc">Validated rule-group evaluation time.</param>
    /// <param name="evaluationDurationSeconds">Validated duration of the alert rule evaluation.</param>
    /// <param name="nowUtc">Current UTC time used to reject impossible timestamps.</param>
    /// <returns>A safe application message without arbitrary annotations or raw identity labels.</returns>
    private AlertMessage? ParseAlert(JsonElement alert, string ruleName, DateTimeOffset evaluatedAtUtc,
        double evaluationDurationSeconds, DateTimeOffset nowUtc)
    {
        string state = this.RequiredString(alert, "state");
        if (state is not ("pending" or "firing"))
        {
            throw this.InvalidResponse();
        }
        DateTimeOffset alertActiveAtUtc = this.ParseTimestamp(this.RequiredString(alert, "activeAt"));
        this.ValidateTimestamp(alertActiveAtUtc, nowUtc);
        bool isRecovery = _recoveryRuleSources.TryGetValue(ruleName, out string? originalRuleName);
        DateTimeOffset activeAtUtc;
        DateTimeOffset? recoveryAtUtc = null;
        if (isRecovery)
        {
            long sourceActiveAtUnixSeconds = this.ParseUnixTimestamp(this.RequiredString(alert, "value"), nowUtc);
            try
            {
                activeAtUtc = DateTimeOffset.FromUnixTimeSeconds(sourceActiveAtUnixSeconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw this.InvalidResponse();
            }
            if (activeAtUtc > alertActiveAtUtc)
            {
                throw this.InvalidResponse();
            }
            if (state == "firing")
            {
                recoveryAtUtc = alertActiveAtUtc;
            }
        }
        else
        {
            activeAtUtc = alertActiveAtUtc;
        }

        JsonElement labels = this.RequiredProperty(alert, "labels", JsonValueKind.Object);
        string job = this.RequiredLabel(labels, "job");
        string instance = this.RequiredLabel(labels, "instance");
        string severity = this.RequiredLabel(labels, "severity");
        string scope = this.RequiredLabel(labels, "scope");
        string? deviceLabel = this.OptionalLabel(labels, "device_id");
        string identityRuleName = originalRuleName ?? ruleName;
        string expectedScope = identityRuleName == "SonosDeviceDescriptionUnavailable" ? "device" : "collection";
        string expectedSeverity = isRecovery ? "info" : "warning";
        if (job != "sonosmonitoring-api" || severity != expectedSeverity || scope != expectedScope ||
            (expectedScope == "device" && deviceLabel is null) ||
            (deviceLabel is not null && Regex.IsMatch(deviceLabel, @"\A[a-f0-9]{32}\z",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) == false))
        {
            throw this.InvalidResponse();
        }

        string identity = this.HashIdentity(identityRuleName, job, instance, deviceLabel);
        string occurrenceIdentity = this.HashOccurrence(identity, activeAtUtc.ToUnixTimeSeconds());
        if (isRecovery && state == "pending")
        {
            return null;
        }
        string outputState = isRecovery ? "recovered" : state;
        return new AlertMessage(identity, occurrenceIdentity, identityRuleName, outputState, activeAtUtc,
            recoveryAtUtc, evaluatedAtUtc, evaluationDurationSeconds, severity, scope, deviceLabel);
    }

    /// <summary>Validates a Prometheus sample value that encodes an original alert activation second.</summary>
    /// <param name="value">Prometheus string representation of the recording-rule value.</param>
    /// <param name="nowUtc">Current UTC time used to reject future sample values.</param>
    /// <returns>The validated whole Unix timestamp in seconds.</returns>
    private long ParseUnixTimestamp(string value, DateTimeOffset nowUtc)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) == false ||
            double.IsFinite(result) == false || result < 0 || result > nowUtc.ToUnixTimeSeconds() ||
            result != Math.Truncate(result) || result > long.MaxValue)
        {
            throw this.InvalidResponse();
        }
        return (long)result;
    }

    /// <summary>Rejects timestamps before the Unix epoch or later than the current time.</summary>
    /// <param name="valueUtc">Timestamp to validate.</param>
    /// <param name="nowUtc">Current UTC time.</param>
    private void ValidateTimestamp(DateTimeOffset valueUtc, DateTimeOffset nowUtc)
    {
        if (valueUtc < DateTimeOffset.UnixEpoch || valueUtc > nowUtc)
        {
            throw this.InvalidResponse();
        }
    }

    /// <summary>Hashes only the stable allowlisted labels used to correlate messages.</summary>
    /// <param name="ruleName">Original allowlisted alert rule name.</param>
    /// <param name="job">Validated fixed Prometheus job label.</param>
    /// <param name="instance">Private instance label, used only as hash input.</param>
    /// <param name="deviceLabel">Optional pseudonymous device label.</param>
    /// <returns>Stable opaque identity shared by activation and recovery messages.</returns>
    private string HashIdentity(string ruleName, string job, string instance, string? deviceLabel)
    {
        string material = JsonSerializer.Serialize(new[] { ruleName, job, instance, deviceLabel ?? string.Empty });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    /// <summary>Creates a stable per-activation identity using Prometheus' integer-second state timestamp.</summary>
    /// <param name="identity">Stable identity for the rule and source labels.</param>
    /// <param name="activeAtUnixSeconds">Original alert activation timestamp, normalized to Unix seconds.</param>
    /// <returns>Opaque SHA-256 identity for this activation.</returns>
    private string HashOccurrence(string identity, long activeAtUnixSeconds)
    {
        string material = string.Concat(identity, ":", activeAtUnixSeconds.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    /// <summary>Returns an object property only when it has the required JSON kind.</summary>
    /// <param name="element">Parent JSON value.</param>
    /// <param name="propertyName">Required property name.</param>
    /// <param name="kind">Required JSON kind.</param>
    /// <returns>The validated property value.</returns>
    private JsonElement RequiredProperty(JsonElement element, string propertyName, JsonValueKind kind)
    {
        if (element.ValueKind != JsonValueKind.Object || element.TryGetProperty(propertyName, out JsonElement value) == false ||
            value.ValueKind != kind)
        {
            throw this.InvalidResponse();
        }
        return value;
    }

    /// <summary>Reads a required nonempty JSON string.</summary>
    /// <param name="element">Object containing the string.</param>
    /// <param name="propertyName">Required property name.</param>
    /// <returns>The validated string value.</returns>
    private string RequiredString(JsonElement element, string propertyName)
    {
        JsonElement value = this.RequiredProperty(element, propertyName, JsonValueKind.String);
        string? result = value.GetString();
        if (string.IsNullOrWhiteSpace(result))
        {
            throw this.InvalidResponse();
        }
        return result;
    }

    /// <summary>Reads a required finite nonnegative number.</summary>
    /// <param name="element">Object containing the number.</param>
    /// <param name="propertyName">Required property name.</param>
    /// <returns>The validated number.</returns>
    private double RequiredNonNegativeNumber(JsonElement element, string propertyName)
    {
        JsonElement value = this.RequiredProperty(element, propertyName, JsonValueKind.Number);
        if (value.TryGetDouble(out double result) == false || double.IsFinite(result) == false || result < 0)
        {
            throw this.InvalidResponse();
        }
        return result;
    }

    /// <summary>Reads a required nonempty string label.</summary>
    /// <param name="labels">Alert labels object.</param>
    /// <param name="name">Required label name.</param>
    /// <returns>The validated label value.</returns>
    private string RequiredLabel(JsonElement labels, string name)
    {
        JsonElement value = this.RequiredProperty(labels, name, JsonValueKind.String);
        string? result = value.GetString();
        if (string.IsNullOrWhiteSpace(result))
        {
            throw this.InvalidResponse();
        }
        return result;
    }

    /// <summary>Reads an optional string label without exposing other upstream labels.</summary>
    /// <param name="labels">Alert labels object.</param>
    /// <param name="name">Optional label name.</param>
    /// <returns>The label value, or null when absent.</returns>
    private string? OptionalLabel(JsonElement labels, string name)
    {
        if (labels.TryGetProperty(name, out JsonElement value) == false)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw this.InvalidResponse();
        }
        return value.GetString();
    }

    /// <summary>Parses a timestamp and normalizes it to UTC.</summary>
    /// <param name="value">RFC 3339 timestamp from Prometheus.</param>
    /// <returns>The parsed UTC timestamp.</returns>
    private DateTimeOffset ParseTimestamp(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset result) == false)
        {
            throw this.InvalidResponse();
        }
        return result;
    }

    /// <summary>Rejects missing, stale, or future rule evaluations.</summary>
    /// <param name="evaluatedAtUtc">Evaluation timestamp from Prometheus.</param>
    /// <param name="nowUtc">Current UTC time.</param>
    private void ValidateFresh(DateTimeOffset evaluatedAtUtc, DateTimeOffset nowUtc)
    {
        TimeSpan age = nowUtc - evaluatedAtUtc;
        if (age < TimeSpan.Zero || age > _maxEvaluationAge)
        {
            throw this.InvalidResponse();
        }
    }

    /// <summary>Creates a generic upstream validation error without copying potentially sensitive response data.</summary>
    /// <returns>A bounded upstream failure exception.</returns>
    private HttpRequestException InvalidResponse() => new("Prometheus alert data failed validation.");
}
