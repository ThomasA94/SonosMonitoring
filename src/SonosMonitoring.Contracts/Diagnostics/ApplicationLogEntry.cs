namespace SonosMonitoring.Contracts.Diagnostics;

/// <summary>One bounded, sanitized application diagnostic event.</summary>
/// <param name="TimestampUtc">UTC time at which the provider received the event.</param>
/// <param name="Source">Allowlisted application host: api, web, or projections.</param>
/// <param name="Level">Microsoft logging severity name.</param>
/// <param name="Category">Sanitized logger category.</param>
/// <param name="EventId">Numeric logger event identifier.</param>
/// <param name="Message">Sanitized message without exception stack traces.</param>
public sealed record ApplicationLogEntry(DateTimeOffset TimestampUtc, string Source, string Level,
    string Category, int EventId, string Message);
