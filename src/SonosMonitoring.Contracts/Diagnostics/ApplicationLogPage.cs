namespace SonosMonitoring.Contracts.Diagnostics;

/// <summary>A bounded view of retained application events, ordered newest first.</summary>
/// <param name="Entries">Matching retained events, newest first.</param>
/// <param name="Sources">Storage availability for each selected source.</param>
/// <param name="Truncated">Whether additional matching events exceeded the response limit.</param>
/// <param name="Limit">Maximum number of returned events.</param>
public sealed record ApplicationLogPage(IReadOnlyList<ApplicationLogEntry> Entries,
    IReadOnlyList<ApplicationLogSourceStatus> Sources, bool Truncated, int Limit);
