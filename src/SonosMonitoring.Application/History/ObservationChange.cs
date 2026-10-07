namespace SonosMonitoring.Application.History;

/// <summary>A persisted observation transition, baseline, or explicit coverage boundary.</summary>
/// <param name="Sequence">Database-local monotonic paging cursor.</param>
/// <param name="Kind">Baseline, change, or gap.</param>
/// <param name="RecordedAtUtc">Time the journal sampled the evidence.</param>
/// <param name="ObservedAtUtc">Source attempt time for a device transition.</param>
/// <param name="Before">Previously retained evidence, if available.</param>
/// <param name="After">Current evidence, if this is a device observation.</param>
/// <param name="Reason">Bounded coverage-boundary category for gap records.</param>
public sealed record ObservationChange(long Sequence, string Kind, DateTimeOffset RecordedAtUtc,
    DateTimeOffset? ObservedAtUtc, ObservedDevice? Before, ObservedDevice? After, string? Reason);
