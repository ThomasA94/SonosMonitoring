namespace SonosMonitoring.Contracts.Collection;

/// <summary>Transport view of bounded topology candidate scheduling; not a count of identified players.</summary>
/// <param name="LastSelectionUtc">Latest selection time.</param>
/// <param name="CandidateCount">Additional hints scheduled in the pass.</param>
/// <param name="LimitReached">Whether the candidate cap deferred further eligible hints.</param>
public sealed record TopologyDiscoverySnapshotDto(DateTimeOffset? LastSelectionUtc, int CandidateCount, bool LimitReached);
