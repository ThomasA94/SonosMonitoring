namespace SonosMonitoring.Contracts.MediaControl;

/// <summary>Reports authoritative control permission and unresolved target uncertainty.</summary>
/// <param name="Enabled">Whether new control requests may be admitted.</param>
/// <param name="Generation">Opaque generation bound to newly admitted requests.</param>
/// <param name="Blocks">Persistent uncertain operation blocks.</param>
public sealed record MediaControlStatusDto(bool Enabled, string Generation, IReadOnlyList<MediaBlockDto> Blocks);
