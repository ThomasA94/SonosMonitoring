namespace SonosMonitoring.Contracts.Alerts;

/// <summary>Transport view of current alert and explicit recovery messages with validated evaluation metadata.</summary>
/// <param name="EvaluatedAtUtc">UTC time of the last successful evaluation.</param>
/// <param name="EvaluationDurationSeconds">Duration of the complete rule-group evaluation in seconds.</param>
/// <param name="Messages">Current pending and firing instances plus positively confirmed recoveries.</param>
public sealed record AlertMessagesSnapshotDto(DateTimeOffset EvaluatedAtUtc, double EvaluationDurationSeconds,
    IReadOnlyList<AlertMessageDto> Messages);
