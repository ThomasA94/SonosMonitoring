namespace SonosMonitoring.Contracts.Diagnostics;

/// <summary>Describes access to one source's retained logs, independently of its process health.</summary>
/// <param name="Source">Allowlisted application host name.</param>
/// <param name="State">Storage state: available, missing, unavailable, or disabled.</param>
/// <param name="SkippedLines">Malformed or incomplete records skipped during this read.</param>
public sealed record ApplicationLogSourceStatus(string Source, string State, int SkippedLines);
