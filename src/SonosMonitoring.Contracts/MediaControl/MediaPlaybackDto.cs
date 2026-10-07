namespace SonosMonitoring.Contracts.MediaControl;

/// <summary>Reports freshly read local playback data and currently available named intentions.</summary>
/// <param name="Context">Validated target and media context.</param>
/// <param name="ObservedAtUtc">Observation timestamp.</param>
/// <param name="Source">Queue, radio, TV, line-in or unknown.</param>
/// <param name="TransportState">Reported transport state.</param>
/// <param name="Volume">Player volume; never inferred from a group.</param>
/// <param name="Muted">Player mute.</param>
/// <param name="GroupVolume">Independently reported group volume.</param>
/// <param name="GroupMuted">Independently reported group mute.</param>
/// <param name="Title">Reported title.</param>
/// <param name="Artist">Reported artist.</param>
/// <param name="Album">Reported album.</param>
/// <param name="DurationSeconds">Known finite duration.</param>
/// <param name="PositionSeconds">Known absolute position.</param>
/// <param name="AvailableActions">Named intentions available for this source and hardware.</param>
/// <param name="PlayMode">Reported transport play mode.</param>
/// <param name="Crossfade">Reported crossfade setting.</param>
public sealed record MediaPlaybackDto(MediaTargetContextDto Context, DateTimeOffset ObservedAtUtc, string Source,
    string TransportState, int? Volume, bool? Muted, int? GroupVolume, bool? GroupMuted, string? Title,
    string? Artist, string? Album, double? DurationSeconds, double? PositionSeconds,
    IReadOnlyList<string> AvailableActions, string? PlayMode, bool? Crossfade);
