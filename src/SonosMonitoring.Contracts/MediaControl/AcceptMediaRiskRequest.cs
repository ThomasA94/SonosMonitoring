namespace SonosMonitoring.Contracts.MediaControl;

/// <summary>Requires explicit acknowledgement before releasing named uncertain operations.</summary>
/// <param name="OperationIds">Exactly identified uncertain operations.</param>
/// <param name="ConfirmDelayedEffect">Acknowledges that an old write may still take effect.</param>
public sealed record AcceptMediaRiskRequest(IReadOnlyList<Guid> OperationIds, bool ConfirmDelayedEffect);
