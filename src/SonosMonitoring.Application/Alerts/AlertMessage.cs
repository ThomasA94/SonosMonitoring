namespace SonosMonitoring.Application.Alerts;

/// <summary>One currently pending or firing Prometheus alert, reduced to safe fields.</summary>
/// <param name="Identity">Stable SHA-256 identity derived from the alert rule and selected labels.</param>
/// <param name="OccurrenceIdentity">Stable SHA-256 identity for this activation of the alert.</param>
/// <param name="RuleName">One allowlisted SonosMonitoring alert rule name.</param>
/// <param name="State">Current pending, firing, or positively recovered state.</param>
/// <param name="ActiveAtUtc">Time this alert instance first became active.</param>
/// <param name="RecoveryAtUtc">Time positive recovery evidence first became active, when recovered.</param>
/// <param name="EvaluatedAtUtc">Time the containing rule group was most recently evaluated.</param>
/// <param name="EvaluationDurationSeconds">Duration of the rule evaluation in seconds.</param>
/// <param name="Severity">Validated severity label.</param>
/// <param name="Scope">Validated collection or device scope label.</param>
/// <param name="DeviceLabel">Pseudonymous collector metric label for device-scoped messages, when present.</param>
public sealed record AlertMessage(string Identity, string OccurrenceIdentity, string RuleName, string State,
    DateTimeOffset ActiveAtUtc, DateTimeOffset? RecoveryAtUtc, DateTimeOffset EvaluatedAtUtc,
    double EvaluationDurationSeconds, string Severity, string Scope, string? DeviceLabel);
