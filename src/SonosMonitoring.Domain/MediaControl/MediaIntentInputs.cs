namespace SonosMonitoring.Domain.MediaControl;

/// <summary>Structured inputs whose meaning is validated for each named media intention.</summary>
/// <param name="Value">Absolute volume or an on/off value for crossfade.</param>
/// <param name="PositionSeconds">Seek position in seconds.</param>
/// <param name="Reference">Stable media or target reference.</param>
/// <param name="Index">Queue item index.</param>
/// <param name="NewIndex">Queue destination index.</param>
/// <param name="Mode">Repeat or shuffle mode.</param>
public sealed record MediaIntentInputs(int? Value = null, double? PositionSeconds = null,
    string? Reference = null, int? Index = null, int? NewIndex = null, string? Mode = null);
