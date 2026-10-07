namespace SonosMonitoring.Contracts.MediaControl;

/// <summary>Returns a bounded browse page with an explicit source revision.</summary>
/// <param name="Items">Returned media objects.</param>
/// <param name="Offset">Zero-based page offset.</param>
/// <param name="Total">Reported total matches.</param>
/// <param name="Revision">Source revision used by queue preconditions.</param>
public sealed record MediaPageDto(IReadOnlyList<MediaItemDto> Items, int Offset, int Total, string Revision);
