namespace SonosMonitoring.Application.History;

/// <summary>One normalized device observation and its actual source time.</summary>
/// <param name="State">Comparable source evidence.</param>
/// <param name="ObservedAtUtc">Most recent description or topology attempt, not the journal write time.</param>
public sealed record ObservationSample(ObservedDevice State, DateTimeOffset? ObservedAtUtc);
