namespace SonosMonitoring.Application.Collection;

/// <summary>Progress of optional active discovery; silence does not mean that players are offline.</summary>
/// <param name="Enabled">Whether SSDP and the collector are enabled.</param>
/// <param name="LastAttemptUtc">Latest search start, or null before the first search.</param>
/// <param name="LastCompletedUtc">Latest search completion; null for an interrupted attempt.</param>
/// <param name="CandidateCount">Distinct approved usable addresses in this attempt, before player validation.</param>
/// <param name="FailedInterfaces">Number of selected interfaces with socket failures.</param>
/// <param name="LimitReached">Whether at least one receive-count budget was exhausted.</param>
/// <param name="ErrorCode">Bounded error category for partial or interrupted discovery.</param>
public sealed record SsdpSnapshot(bool Enabled, DateTimeOffset? LastAttemptUtc, DateTimeOffset? LastCompletedUtc,
    int CandidateCount, int FailedInterfaces, bool LimitReached, string? ErrorCode);
