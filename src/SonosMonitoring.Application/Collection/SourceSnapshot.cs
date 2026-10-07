namespace SonosMonitoring.Application.Collection;

/// <summary>Tracks the latest attempt and the age of the last good observation for one source.</summary>
/// <param name="Name">Bounded source name.</param>
/// <param name="LastAttemptSucceeded">Whether the latest attempt succeeded.</param>
/// <param name="LastAttemptUtc">Latest attempt time.</param>
/// <param name="LastSuccessUtc">Latest good observation time.</param>
/// <param name="IsStale">Whether the last good observation has expired.</param>
/// <param name="ErrorCode">Bounded failure category without exception content.</param>
public sealed record SourceSnapshot(
    string Name,
    bool LastAttemptSucceeded,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessUtc,
    bool IsStale,
    string? ErrorCode);
