namespace SonosMonitoring.Application.Alerts;

/// <summary>Current internal messages, explicit recovery evidence, and their successful Prometheus rule evaluation.</summary>
/// <param name="EvaluatedAtUtc">UTC time of the last successful evaluation.</param>
/// <param name="EvaluationDurationSeconds">Duration of the complete rule-group evaluation in seconds.</param>
/// <param name="Messages">Current pending and firing instances plus positively confirmed recoveries.</param>
public sealed record AlertMessagesSnapshot(DateTimeOffset EvaluatedAtUtc, double EvaluationDurationSeconds,
    IReadOnlyList<AlertMessage> Messages);
