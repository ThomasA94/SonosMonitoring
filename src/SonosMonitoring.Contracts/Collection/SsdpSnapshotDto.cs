namespace SonosMonitoring.Contracts.Collection;

/// <summary>Transport view of optional discovery; candidates are not validated player identities.</summary>
/// <param name="Enabled">Whether SSDP and the collector are enabled.</param>
/// <param name="LastAttemptUtc">Latest search start.</param>
/// <param name="LastCompletedUtc">Latest completion, or null for an interrupted attempt.</param>
/// <param name="CandidateCount">Distinct approved usable candidate addresses.</param>
/// <param name="FailedInterfaces">Selected interfaces that failed.</param>
/// <param name="LimitReached">Whether a receive-count budget was exhausted.</param>
/// <param name="ErrorCode">Safe partial-result or interruption category.</param>
public sealed record SsdpSnapshotDto(bool Enabled, DateTimeOffset? LastAttemptUtc, DateTimeOffset? LastCompletedUtc,
    int CandidateCount, int FailedInterfaces, bool LimitReached, string? ErrorCode);
