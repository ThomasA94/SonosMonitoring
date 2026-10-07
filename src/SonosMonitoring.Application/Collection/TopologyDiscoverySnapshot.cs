namespace SonosMonitoring.Application.Collection;

/// <summary>Latest topology-hint scheduling result, separate from actual HTTP discovery success.</summary>
/// <param name="LastSelectionUtc">Latest candidate selection time.</param>
/// <param name="CandidateCount">Additional address hints scheduled in that pass.</param>
/// <param name="LimitReached">Whether eligible hints were deferred by the per-pass cap.</param>
public sealed record TopologyDiscoverySnapshot(DateTimeOffset? LastSelectionUtc, int CandidateCount, bool LimitReached);
