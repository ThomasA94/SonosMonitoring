namespace SonosMonitoring.Application.MediaControl.Transport;

/// <summary>One browsed object without exposing its playback URI or DIDL metadata.</summary>
/// <param name="Reference">Opaque expiring server-issued reference.</param>
/// <param name="Title">Reported title.</param>
/// <param name="Artist">Optional artist.</param>
/// <param name="Album">Optional album.</param>
/// <param name="Index">One-based queue position, if applicable.</param>
public sealed record MediaItem(string Reference, string Title, string? Artist, string? Album, int? Index);
