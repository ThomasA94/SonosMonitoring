namespace SonosMonitoring.Application.MediaControl.Transport;

/// <summary>Fresh local media observation with unknown values left nullable.</summary>
/// <param name="Context">Validated current context.</param>
/// <param name="ObservedAtUtc">Read completion time.</param>
/// <param name="Source">Queue, radio, TV, line-in, or unknown.</param>
/// <param name="TransportState">Reported transport state.</param>
/// <param name="Volume">Player volume.</param>
/// <param name="Muted">Player mute.</param>
/// <param name="GroupVolume">Reported group volume.</param>
/// <param name="GroupMuted">Reported group mute.</param>
/// <param name="Title">Reported title.</param>
/// <param name="Artist">Reported artist.</param>
/// <param name="Album">Reported album.</param>
/// <param name="DurationSeconds">Known duration.</param>
/// <param name="PositionSeconds">Known position.</param>
/// <param name="AvailableActions">Named available intentions.</param>
/// <param name="PlayMode">Reported play mode.</param>
/// <param name="Crossfade">Reported crossfade state.</param>
public sealed record MediaPlayback(MediaTargetContext Context, DateTimeOffset ObservedAtUtc, string Source,
    string TransportState, int? Volume, bool? Muted, int? GroupVolume, bool? GroupMuted, string? Title,
    string? Artist, string? Album, double? DurationSeconds, double? PositionSeconds,
    IReadOnlyList<string> AvailableActions, string? PlayMode, bool? Crossfade);
