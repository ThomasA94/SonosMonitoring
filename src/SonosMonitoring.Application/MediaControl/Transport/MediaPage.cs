namespace SonosMonitoring.Application.MediaControl.Transport;

/// <summary>One bounded browse page and its source revision.</summary>
/// <param name="Items">Browsable items.</param>
/// <param name="Offset">Zero-based offset.</param>
/// <param name="Total">Reported total.</param>
/// <param name="Revision">Revision from the device response.</param>
public sealed record MediaPage(IReadOnlyList<MediaItem> Items, int Offset, int Total, string Revision);
