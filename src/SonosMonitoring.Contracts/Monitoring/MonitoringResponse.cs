namespace SonosMonitoring.Contracts.Monitoring;

/// <summary>Projected policy with enough metadata to recognize delayed updates.</summary>
/// <param name="AggregateId">Stable stream identity.</param>
/// <param name="DeviceKey">Stable Sonos UDN without the uuid prefix.</param>
/// <param name="Enabled">Current projected policy.</param>
/// <param name="IntervalSeconds">Requested polling interval.</param>
/// <param name="EventSequence">Last projected event.</param>
/// <param name="UpdatedAtUtc">Time the document was written.</param>
public sealed record MonitoringResponse(string AggregateId, string DeviceKey, bool Enabled, int IntervalSeconds, long EventSequence, DateTimeOffset UpdatedAtUtc)
{
    /// <summary>Gets the opaque persisted commit token corresponding to this projected position, when available.</summary>
    public string? ConcurrencyToken { get; init; }
}
