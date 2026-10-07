namespace SonosMonitoring.Contracts.History;

/// <summary>A bounded journal page; retention can remove older cursors between requests.</summary>
/// <param name="Changes">Retained changes in ascending sequence.</param>
/// <param name="NextCursor">Cursor to continue after the last returned entry.</param>
/// <param name="EarliestCursor">Earliest retained sequence across all devices.</param>
/// <param name="LastRecordedAtUtc">Latest successful sampling time, including unchanged state.</param>
/// <param name="RetentionDays">Maximum retained age.</param>
/// <param name="MaxEntries">Maximum retained entry count.</param>
public sealed record ObservationPageDto(IReadOnlyList<ObservationChangeDto> Changes, long NextCursor, long? EarliestCursor,
    DateTimeOffset? LastRecordedAtUtc, int RetentionDays, int MaxEntries);
