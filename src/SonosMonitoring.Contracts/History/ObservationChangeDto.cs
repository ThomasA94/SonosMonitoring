namespace SonosMonitoring.Contracts.History;

/// <summary>A retained sampled transition or explicit coverage boundary.</summary>
/// <param name="Sequence">Database-local exclusive paging cursor.</param>
/// <param name="Kind">Baseline, change, or gap.</param>
/// <param name="RecordedAtUtc">Journal sampling time.</param>
/// <param name="ObservedAtUtc">Newest relevant source attempt time, if available.</param>
/// <param name="Before">Previously retained state.</param>
/// <param name="After">New source evidence.</param>
/// <param name="Reason">Coverage-boundary category, if any.</param>
public sealed record ObservationChangeDto(long Sequence, string Kind, DateTimeOffset RecordedAtUtc,
    DateTimeOffset? ObservedAtUtc, ObservedDeviceDto? Before, ObservedDeviceDto? After, string? Reason);
