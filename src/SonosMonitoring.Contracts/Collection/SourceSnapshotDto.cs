namespace SonosMonitoring.Contracts.Collection;

/// <summary>Transport view of one independently failing collector source.</summary>
/// <param name="Name">Bounded source name.</param>
/// <param name="LastAttemptSucceeded">Whether the latest attempt succeeded.</param>
/// <param name="LastAttemptUtc">Latest attempt time.</param>
/// <param name="LastSuccessUtc">Latest good observation time.</param>
/// <param name="IsStale">Whether the latest good observation has expired.</param>
/// <param name="ErrorCode">Bounded status code without exception details.</param>
public sealed record SourceSnapshotDto(
    string Name,
    bool LastAttemptSucceeded,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessUtc,
    bool IsStale,
    string? ErrorCode);
