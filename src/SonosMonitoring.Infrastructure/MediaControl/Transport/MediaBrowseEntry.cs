namespace SonosMonitoring.Infrastructure.MediaControl.Transport;

/// <summary>Private DIDL object data retained only behind an opaque reference.</summary>
/// <param name="ObjectId">Original ContentDirectory object ID.</param>
/// <param name="Title">Display title.</param>
/// <param name="Artist">Display artist.</param>
/// <param name="Album">Display album.</param>
/// <param name="Uri">Playable resource URI, if advertised.</param>
/// <param name="Metadata">Original bounded DIDL element.</param>
public sealed record MediaBrowseEntry(string ObjectId, string Title, string? Artist, string? Album, string? Uri,
    string Metadata);
