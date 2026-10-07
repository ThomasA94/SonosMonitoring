namespace SonosMonitoring.Contracts.MediaControl;

/// <summary>Separates durable operation lifecycle from observations of the loudspeaker.</summary>
/// <param name="OperationId">Stable retry identity.</param>
/// <param name="Action">Named user intention.</param>
/// <param name="DeviceKey">Bound player.</param>
/// <param name="Status">Durable execution status.</param>
/// <param name="AcceptedAtUtc">Durable admission time.</param>
/// <param name="Code">Safe outcome or rejection code.</param>
/// <param name="CommitToken">Confirmed operation commit token.</param>
/// <param name="EventSequence">Confirmed per-operation event position.</param>
/// <param name="Step">Zero-based last dispatch step, or minus one before dispatch began.</param>
public sealed record MediaOperationDto(Guid OperationId, string Action, string DeviceKey, string Status,
    DateTimeOffset AcceptedAtUtc, string? Code, string? CommitToken, long? EventSequence, int Step = -1);
