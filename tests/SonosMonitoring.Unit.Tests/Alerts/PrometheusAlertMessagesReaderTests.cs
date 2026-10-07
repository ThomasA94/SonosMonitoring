using System.Text.Json;
using SonosMonitoring.Application.Alerts;
using SonosMonitoring.Infrastructure.Collection;
using SonosMonitoring.Infrastructure.Alerts;

namespace SonosMonitoring.Unit.Tests.Alerts;

/// <summary>Verifies bounded, fail-closed reads of current Prometheus alert messages.</summary>
public sealed class PrometheusAlertMessagesReaderTests
{
    private static readonly string[] _expectedRuleNames =
    [
        "SonosApiUnavailable",
        "SonosCollectorInterrupted",
        "SonosDeviceDescriptionUnavailable",
        "SonosApiRecovered",
        "SonosCollectorRecovered",
        "SonosDeviceDescriptionRecovered"
    ];
    private static readonly string _firstDeviceLabel = CollectorMetricsFormatter.DeviceLabel("RINCON_PRIVATE_ONE");
    private static readonly string _secondDeviceLabel = CollectorMetricsFormatter.DeviceLabel("RINCON_PRIVATE_TWO");

    /// <summary>Maps active and recovered fields and hashes identity labels without forwarding annotations.</summary>
    [Fact]
    public async Task ReadAsyncReturnsSafeCurrentMessagesWithStableHashedIdentity()
    {
        DateTimeOffset evaluatedAt = DateTimeOffset.UtcNow.AddSeconds(-2);
        DateTimeOffset firstActiveAt = DateTimeOffset.UtcNow.AddMinutes(-4);
        DateTimeOffset secondActiveAt = DateTimeOffset.UtcNow.AddMinutes(-3);
        DateTimeOffset recoveryAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        List<Dictionary<string, object?>> alerts =
        [
            this.CreateAlert("pending", "192.0.2.40:9100", _firstDeviceLabel, activeAt: firstActiveAt),
            this.CreateAlert("firing", "192.0.2.40:9100", _secondDeviceLabel, activeAt: secondActiveAt)
        ];
        List<Dictionary<string, object?>> recoveries =
        [
            this.CreateAlert("firing", "192.0.2.40:9100", _firstDeviceLabel, "info",
                firstActiveAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), recoveryAt)
        ];
        string body = this.CreateResponse(evaluatedAt, alerts);
        string? requestUri = null;
        using StubHttpMessageHandler handler = new((request, _) =>
        {
            requestUri = request.RequestUri?.ToString();
            return Task.FromResult(JsonResponse(body));
        });
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://prometheus.internal/") };
        PrometheusAlertMessagesReader reader = new(client);

        var first = await reader.ReadAsync(CancellationToken.None);
        var second = await reader.ReadAsync(CancellationToken.None);
        body = this.CreateResponse(evaluatedAt, recoveryAlerts: recoveries);
        var recovery = await reader.ReadAsync(CancellationToken.None);
        var repeatedRecovery = await reader.ReadAsync(CancellationToken.None);
        AlertMessage recovered = Assert.Single(recovery.Messages);
        string json = JsonSerializer.Serialize(new[] { first, recovery }, JsonSerializerOptions.Web);

        Assert.Equal("http://prometheus.internal/api/v1/rules", requestUri);
        Assert.Equal(evaluatedAt, first.EvaluatedAtUtc);
        Assert.Equal(0.05, first.EvaluationDurationSeconds);
        Assert.Equal(2, first.Messages.Count);
        Assert.Equal(new[] { "pending", "firing" }, first.Messages.Select(message => message.State));
        Assert.Equal("recovered", recovered.State);
        Assert.All(first.Messages, message => Assert.Equal(evaluatedAt, message.EvaluatedAtUtc));
        Assert.All(first.Messages, message => Assert.Equal(0.025, message.EvaluationDurationSeconds));
        Assert.All(first.Messages, message => Assert.Equal("warning", message.Severity));
        Assert.Equal("info", recovered.Severity);
        Assert.All(first.Messages, message => Assert.Equal("device", message.Scope));
        Assert.Equal(new[] { _firstDeviceLabel, _secondDeviceLabel }, first.Messages.Select(message => message.DeviceLabel));
        Assert.Equal(_firstDeviceLabel, recovered.DeviceLabel);
        Assert.All(first.Messages, message => Assert.Matches("^[a-f0-9]{64}$", message.Identity));
        Assert.All(first.Messages, message => Assert.Matches("^[a-f0-9]{64}$", message.OccurrenceIdentity));
        Assert.Equal(first.Messages.Select(message => message.Identity), second.Messages.Select(message => message.Identity));
        Assert.NotEqual(first.Messages[0].Identity, first.Messages[1].Identity);
        Assert.Equal(first.Messages[0].Identity, recovered.Identity);
        Assert.Equal(first.Messages[0].OccurrenceIdentity, recovered.OccurrenceIdentity);
        Assert.Equal(recovered.OccurrenceIdentity, Assert.Single(repeatedRecovery.Messages).OccurrenceIdentity);
        Assert.NotEqual(first.Messages[0].OccurrenceIdentity, first.Messages[1].OccurrenceIdentity);
        Assert.Equal(firstActiveAt.ToUnixTimeSeconds(), recovered.ActiveAtUtc.ToUnixTimeSeconds());
        Assert.Equal(recoveryAt, recovered.RecoveryAtUtc);
        Assert.Equal("SonosDeviceDescriptionUnavailable", recovered.RuleName);
        Assert.Contains(_firstDeviceLabel, json, StringComparison.Ordinal);
        Assert.Contains(_secondDeviceLabel, json, StringComparison.Ordinal);
        Assert.DoesNotContain("RINCON_PRIVATE", json, StringComparison.Ordinal);
        Assert.Contains("deviceLabel", json, StringComparison.Ordinal);
        Assert.DoesNotContain("192.0.2.40", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_ANNOTATION", json, StringComparison.Ordinal);
        Assert.DoesNotContain("runbook_url", json, StringComparison.Ordinal);
    }

    /// <summary>Maps only firing recovery evidence and omits pending recovery candidates.</summary>
    [Fact]
    public async Task ReadAsyncMapsFiringRecoveryAndSkipsPendingRecovery()
    {
        DateTimeOffset evaluatedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        DateTimeOffset originalActiveAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        DateTimeOffset recoveryStartedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        List<Dictionary<string, object?>> recoveryAlerts =
        [
            this.CreateAlert("pending", "192.0.2.40:9100", _firstDeviceLabel, "info",
                originalActiveAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), recoveryStartedAt),
            this.CreateAlert("firing", "192.0.2.40:9100", _secondDeviceLabel, "info",
                originalActiveAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), recoveryStartedAt)
        ];
        using StubHttpMessageHandler handler = new((_, _) =>
            Task.FromResult(JsonResponse(this.CreateResponse(evaluatedAt, recoveryAlerts: recoveryAlerts))));
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://prometheus.internal/") };

        var snapshot = await new PrometheusAlertMessagesReader(client).ReadAsync(CancellationToken.None);

        AlertMessage recovered = Assert.Single(snapshot.Messages);
        Assert.Equal("recovered", recovered.State);
        Assert.Equal("SonosDeviceDescriptionUnavailable", recovered.RuleName);
        Assert.Equal(_secondDeviceLabel, recovered.DeviceLabel);
        Assert.Equal("info", recovered.Severity);
        Assert.NotNull(recovered.RecoveryAtUtc);
    }

    /// <summary>Treats a recent healthy evaluation with no active alerts as a valid empty current state.</summary>
    [Fact]
    public async Task ReadAsyncReturnsEmptyOnlyAfterAllExpectedRulesEvaluateHealthily()
    {
        DateTimeOffset evaluatedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        using StubHttpMessageHandler handler = new((_, _) => Task.FromResult(JsonResponse(this.CreateResponse(evaluatedAt))));
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://prometheus.internal/") };

        var snapshot = await new PrometheusAlertMessagesReader(client).ReadAsync(CancellationToken.None);

        Assert.Empty(snapshot.Messages);
        Assert.Equal(evaluatedAt, snapshot.EvaluatedAtUtc);
    }

    /// <summary>Rejects stale, unhealthy, unknown, or incomplete rule groups instead of returning clear state.</summary>
    [Fact]
    public async Task ReadAsyncRejectsUnverifiedEmptyResults()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string[] invalidResponses =
        [
            this.CreateResponse(now.AddSeconds(-61)),
            this.CreateResponse(now.AddSeconds(-1), health: "err"),
            this.CreateResponse(now.AddSeconds(-1), omitRule: "SonosCollectorInterrupted"),
            this.CreateResponse(now.AddSeconds(-1), omitRule: "sonos:collector_observing"),
            this.CreateResponse(now.AddSeconds(-1), groupName: "unexpected")
        ];

        foreach (string body in invalidResponses)
        {
            using StubHttpMessageHandler handler = new((_, _) => Task.FromResult(JsonResponse(body)));
            using HttpClient client = new(handler) { BaseAddress = new Uri("http://prometheus.internal/") };
            await Assert.ThrowsAsync<HttpRequestException>(() => new PrometheusAlertMessagesReader(client).ReadAsync(CancellationToken.None));
        }
    }

    /// <summary>Rejects excessive active messages and oversized responses rather than truncating them.</summary>
    [Fact]
    public async Task ReadAsyncRejectsMessageAndBodyLimits()
    {
        DateTimeOffset evaluatedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        List<Dictionary<string, object?>> alerts = Enumerable.Range(0, 1025)
            .Select(index => this.CreateAlert("firing", "192.0.2.40:9100",
                CollectorMetricsFormatter.DeviceLabel("RINCON_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture))))
            .ToList();
        string tooManyMessages = this.CreateResponse(evaluatedAt, alerts);
        using (StubHttpMessageHandler handler = new((_, _) => Task.FromResult(JsonResponse(tooManyMessages))))
        using (HttpClient client = new(handler) { BaseAddress = new Uri("http://prometheus.internal/") })
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => new PrometheusAlertMessagesReader(client).ReadAsync(CancellationToken.None));
        }

        string oversized = new(' ', 2 * 1024 * 1024 + 16);
        using StubHttpMessageHandler oversizedHandler = new((_, _) => Task.FromResult(JsonResponse(oversized)));
        using HttpClient oversizedClient = new(oversizedHandler) { BaseAddress = new Uri("http://prometheus.internal/") };
        await Assert.ThrowsAsync<HttpRequestException>(() => new PrometheusAlertMessagesReader(oversizedClient).ReadAsync(CancellationToken.None));
    }

    /// <summary>Rejects device-scoped alerts whose metric label is outside the formatter's hash format.</summary>
    [Fact]
    public async Task ReadAsyncRejectsInvalidAffectedDeviceLabel()
    {
        DateTimeOffset evaluatedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        List<Dictionary<string, object?>> alerts = [this.CreateAlert("firing", "192.0.2.40:9100", "RINCON_PRIVATE_ONE")];
        using StubHttpMessageHandler handler = new((_, _) => Task.FromResult(JsonResponse(this.CreateResponse(evaluatedAt, alerts))));
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://prometheus.internal/") };

        await Assert.ThrowsAsync<HttpRequestException>(() => new PrometheusAlertMessagesReader(client).ReadAsync(CancellationToken.None));
    }

    /// <summary>Rejects invalid recovery timestamps and future or negative episode values.</summary>
    [Fact]
    public async Task ReadAsyncRejectsInvalidRecoveryEvidence()
    {
        DateTimeOffset evaluatedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        string[] invalidValues = ["-1", "NaN", "Infinity", "1.5", "9999999999"];
        foreach (string value in invalidValues)
        {
            List<Dictionary<string, object?>> recoveryAlerts =
            [this.CreateAlert("firing", "192.0.2.40:9100", _firstDeviceLabel, "info", value)];
            using StubHttpMessageHandler handler = new((_, _) =>
                Task.FromResult(JsonResponse(this.CreateResponse(evaluatedAt, recoveryAlerts: recoveryAlerts))));
            using HttpClient client = new(handler) { BaseAddress = new Uri("http://prometheus.internal/") };
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                new PrometheusAlertMessagesReader(client).ReadAsync(CancellationToken.None));
        }
    }

    /// <summary>Assigns a different occurrence identity when the same stable alert identity fires again.</summary>
    [Fact]
    public async Task ReadAsyncSeparatesRepeatedAlertOccurrences()
    {
        DateTimeOffset evaluatedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        DateTimeOffset firstEpisode = DateTimeOffset.UtcNow.AddMinutes(-8);
        DateTimeOffset secondEpisode = DateTimeOffset.UtcNow.AddMinutes(-4);
        List<Dictionary<string, object?>> firstRecovery =
        [this.CreateAlert("firing", "192.0.2.40:9100", _firstDeviceLabel, "info",
            firstEpisode.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.UtcNow.AddMinutes(-7))];
        List<Dictionary<string, object?>> secondRecovery =
        [this.CreateAlert("firing", "192.0.2.40:9100", _firstDeviceLabel, "info",
            secondEpisode.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.UtcNow.AddMinutes(-3))];
        int callCount = 0;
        using StubHttpMessageHandler handler = new((request, _) =>
        {
            string body = callCount++ == 0
                ? this.CreateResponse(evaluatedAt, recoveryAlerts: firstRecovery)
                : this.CreateResponse(evaluatedAt, recoveryAlerts: secondRecovery);
            return Task.FromResult(JsonResponse(body));
        });
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://prometheus.internal/") };
        PrometheusAlertMessagesReader reader = new(client);

        AlertMessage first = Assert.Single((await reader.ReadAsync(CancellationToken.None)).Messages);
        AlertMessage repeated = Assert.Single((await reader.ReadAsync(CancellationToken.None)).Messages);

        Assert.Equal(first.Identity, repeated.Identity);
        Assert.NotEqual(first.OccurrenceIdentity, repeated.OccurrenceIdentity);
    }

    /// <summary>Fails closed when a response briefly contains both an original alert and its recovery.</summary>
    [Fact]
    public async Task ReadAsyncRejectsContradictoryActiveAndRecoveredMessages()
    {
        DateTimeOffset evaluatedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        DateTimeOffset originalActiveAt = DateTimeOffset.UtcNow.AddMinutes(-4);
        List<Dictionary<string, object?>> activeAlerts =
        [this.CreateAlert("firing", "192.0.2.40:9100", _firstDeviceLabel, activeAt: originalActiveAt)];
        List<Dictionary<string, object?>> recoveryAlerts =
        [this.CreateAlert("firing", "192.0.2.40:9100", _firstDeviceLabel, "info",
            originalActiveAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.UtcNow.AddMinutes(-1))];
        using StubHttpMessageHandler handler = new((_, _) =>
            Task.FromResult(JsonResponse(this.CreateResponse(evaluatedAt, activeAlerts, recoveryAlerts: recoveryAlerts))));
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://prometheus.internal/") };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new PrometheusAlertMessagesReader(client).ReadAsync(CancellationToken.None));
    }

    /// <summary>Creates a well-formed alert object with deliberate private canary annotations.</summary>
    /// <param name="state">Prometheus alert state.</param>
    /// <param name="instance">Instance label included only in the identity hash.</param>
    /// <param name="deviceLabel">Pseudonymous device label included in the identity hash.</param>
    /// <param name="severity">Validated severity label expected for the alert kind.</param>
    /// <param name="value">Prometheus sample value; recovery alerts use the original activation second.</param>
    /// <param name="activeAt">Optional alert active time.</param>
    /// <returns>A serializable Prometheus alert value.</returns>
    private Dictionary<string, object?> CreateAlert(string state, string instance, string deviceLabel,
        string severity = "warning", string value = "1", DateTimeOffset? activeAt = null) => new()
        {
            ["state"] = state,
            ["activeAt"] = (activeAt ?? DateTimeOffset.UtcNow.AddMinutes(-4)).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["value"] = value,
            ["labels"] = new Dictionary<string, string>
            {
                ["job"] = "sonosmonitoring-api",
                ["instance"] = instance,
                ["device_id"] = deviceLabel,
                ["severity"] = severity,
                ["scope"] = "device"
            },
            ["annotations"] = new Dictionary<string, string>
            {
                ["summary"] = "SECRET_ANNOTATION",
                ["runbook_url"] = "https://internal.example/private"
            }
        };

    /// <summary>Builds a Prometheus response for the configured group and exact alert-rule set.</summary>
    /// <param name="evaluatedAt">Shared group and rule evaluation timestamp.</param>
    /// <param name="deviceAlerts">Optional alerts for the device-description rule.</param>
    /// <param name="recoveryAlerts">Optional active recovery alerts.</param>
    /// <param name="health">Health value to apply to each expected rule.</param>
    /// <param name="omitRule">Optional expected rule to omit for validation tests.</param>
    /// <param name="groupName">Rule-group name.</param>
    /// <returns>Serialized response content.</returns>
    private string CreateResponse(DateTimeOffset evaluatedAt, List<Dictionary<string, object?>>? deviceAlerts = null,
        string health = "ok", string? omitRule = null, string groupName = "sonosmonitoring",
        List<Dictionary<string, object?>>? recoveryAlerts = null)
    {
        List<object> rules = [];
        foreach (string name in _expectedRuleNames.Append("sonos:collector_observing")
            .Append("sonos:alert_firing_started_timestamp_seconds"))
        {
            if (name == omitRule)
            {
                continue;
            }
            bool isRecordingRule = name is "sonos:collector_observing" or "sonos:alert_firing_started_timestamp_seconds";
            List<Dictionary<string, object?>> alerts = name == "SonosDeviceDescriptionUnavailable" ? deviceAlerts ?? [] :
                name == "SonosDeviceDescriptionRecovered" ? recoveryAlerts ?? [] : [];
            string state = alerts.Any(alert => (string)alert["state"]! == "firing") ? "firing" : alerts.Count > 0 ? "pending" : "inactive";
            Dictionary<string, object?> rule = new()
            {
                ["name"] = name,
                ["type"] = isRecordingRule ? "recording" : "alerting",
                ["health"] = health,
                ["lastEvaluation"] = evaluatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["evaluationTime"] = 0.025
            };
            if (isRecordingRule == false)
            {
                rule["state"] = state;
                rule["alerts"] = alerts;
            }
            rules.Add(rule);
        }
        return JsonSerializer.Serialize(new
        {
            status = "success",
            data = new
            {
                groups = new[]
                {
                    new
                    {
                        name = groupName,
                        lastEvaluation = evaluatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                        evaluationTime = 0.05,
                        rules
                    }
                }
            }
        });
    }

    /// <summary>Creates a JSON HTTP response for the stub upstream.</summary>
    /// <param name="body">Response body.</param>
    /// <returns>An HTTP 200 JSON response.</returns>
    private static HttpResponseMessage JsonResponse(string body) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };
}
