namespace SonosMonitoring.Contracts.MediaControl;

/// <summary>Displays one browsed media object without exposing credential-bearing playback URIs or XML.</summary>
/// <param name="Reference">Opaque server-issued reference.</param>
/// <param name="Title">Reported title.</param>
/// <param name="Artist">Optional reported artist.</param>
/// <param name="Album">Optional reported album.</param>
/// <param name="Index">One-based queue position, or null for a favorite.</param>
public sealed record MediaItemDto(string Reference, string Title, string? Artist, string? Album, int? Index);
