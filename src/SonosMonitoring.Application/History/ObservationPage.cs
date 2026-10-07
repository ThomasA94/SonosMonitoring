namespace SonosMonitoring.Application.History;

/// <summary>A bounded ascending journal page with retention and recording evidence.</summary>
/// <param name="Changes">Retained changes after the supplied cursor.</param>
/// <param name="NextCursor">Last returned sequence, or the supplied cursor for an empty page.</param>
/// <param name="EarliestCursor">Earliest retained sequence, or null when empty.</param>
/// <param name="LastRecordedAtUtc">Last successful journal sample, including unchanged state.</param>
/// <param name="RetentionDays">Maximum configured age of retained entries.</param>
/// <param name="MaxEntries">Maximum configured number of retained entries.</param>
public sealed record ObservationPage(IReadOnlyList<ObservationChange> Changes, long NextCursor, long? EarliestCursor,
    DateTimeOffset? LastRecordedAtUtc, int RetentionDays, int MaxEntries);
