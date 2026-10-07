namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>One interface's bounded search result, including incomplete search indicators.</summary>
/// <param name="Datagrams">Responses within the size limit; still untrusted.</param>
/// <param name="LimitReached">Whether the receive-count budget ended this search early.</param>
/// <param name="ErrorCode">A safe socket failure category, if any.</param>
public sealed record SsdpSearchResult(IReadOnlyList<SsdpDatagram> Datagrams, bool LimitReached, string? ErrorCode);
