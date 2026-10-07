namespace SonosMonitoring.Contracts.MediaControl;

/// <summary>Supplies transport inputs to a named media intention endpoint; the endpoint owns its meaning.</summary>
/// <param name="Context">Original displayed target context retained for retries.</param>
/// <param name="Value">Absolute volume or explicit crossfade value.</param>
/// <param name="PositionSeconds">Absolute playback position.</param>
/// <param name="Reference">Server-issued media reference or selected coordinator identity.</param>
/// <param name="Index">One-based observed queue item.</param>
/// <param name="NewIndex">One-based destination for queue reordering.</param>
/// <param name="Mode">Explicit playback mode.</param>
/// <param name="DestinationContext">Optional displayed destination precondition for a group join.</param>
public sealed record MediaActionRequest(MediaTargetContextDto Context, int? Value = null,
    double? PositionSeconds = null, string? Reference = null, int? Index = null, int? NewIndex = null, string? Mode = null,
    MediaTargetContextDto? DestinationContext = null);
