namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Bounded address hints selected for independent HTTP identity validation.</summary>
/// <param name="Addresses">Deduplicated approved addresses not already scheduled in this pass.</param>
/// <param name="LimitReached">Whether eligible hints remain beyond this pass's candidate cap.</param>
public sealed record TopologyCandidateSelection(IReadOnlyList<string> Addresses, bool LimitReached);
