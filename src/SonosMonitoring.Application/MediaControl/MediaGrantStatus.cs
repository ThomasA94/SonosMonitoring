namespace SonosMonitoring.Application.MediaControl;

/// <summary>Reports authoritative permission and unresolved execution fences.</summary>
/// <param name="Enabled">Whether new actions can be accepted.</param>
/// <param name="Generation">Current permission generation.</param>
/// <param name="BlockedOperations">Unresolved operations blocking their complete target sets.</param>
public sealed record MediaGrantStatus(bool Enabled, string Generation, IReadOnlyList<CoordinatedMediaOperation> BlockedOperations);
