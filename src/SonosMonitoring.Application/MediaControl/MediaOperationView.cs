using SonosMonitoring.Domain.MediaControl;

namespace SonosMonitoring.Application.MediaControl;

/// <summary>Couples the latest persisted operation state to its exact stream position.</summary>
/// <param name="State">Durable media operation state.</param>
/// <param name="ConcurrencyToken">Opaque token for the next operation transition.</param>
/// <param name="EventSequence">Position of the state in this operation stream.</param>
public sealed record MediaOperationView(MediaOperationState State, string ConcurrencyToken, long EventSequence);
