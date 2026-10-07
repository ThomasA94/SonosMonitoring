namespace SonosMonitoring.Application.MediaControl.Transport;

/// <summary>Bounded values supplied for one named media intention.</summary>
/// <param name="Value">Absolute volume or mute value.</param>
/// <param name="PositionSeconds">Seek position.</param>
/// <param name="Reference">Opaque server-issued media reference.</param>
/// <param name="Index">One-based queue position.</param>
/// <param name="NewIndex">One-based destination position.</param>
/// <param name="Mode">Explicit transport mode.</param>
/// <param name="DestinationContext">Frozen destination context for a join action.</param>
public sealed record MediaWriteInputs(int? Value = null, double? PositionSeconds = null, string? Reference = null,
    int? Index = null, int? NewIndex = null, string? Mode = null, MediaTargetContext? DestinationContext = null);
