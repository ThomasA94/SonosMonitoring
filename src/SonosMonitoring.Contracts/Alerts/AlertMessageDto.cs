namespace SonosMonitoring.Contracts.Alerts;

/// <summary>Safe transport view of one currently pending or firing internal message.</summary>
/// <param name="Identity">Stable opaque alert identity.</param>
/// <param name="OccurrenceIdentity">Stable opaque identity for this alert activation.</param>
/// <param name="RuleName">Allowlisted SonosMonitoring rule name.</param>
/// <param name="State">Current pending, firing, or positively recovered state.</param>
/// <param name="ActiveAtUtc">Time this alert instance first became active.</param>
/// <param name="RecoveryAtUtc">Time positive recovery evidence first became active, when recovered.</param>
/// <param name="EvaluatedAtUtc">Time the containing rule group was evaluated.</param>
/// <param name="EvaluationDurationSeconds">Duration of this alert rule evaluation.</param>
/// <param name="Severity">Validated severity label.</param>
/// <param name="Scope">Validated collection or device scope.</param>
/// <param name="DeviceLabel">Pseudonymous collector metric label for device-scoped messages, when present.</param>
public sealed record AlertMessageDto(string Identity, string OccurrenceIdentity, string RuleName, string State,
    DateTimeOffset ActiveAtUtc, DateTimeOffset? RecoveryAtUtc, DateTimeOffset EvaluatedAtUtc,
    double EvaluationDurationSeconds, string Severity, string Scope, string? DeviceLabel);
