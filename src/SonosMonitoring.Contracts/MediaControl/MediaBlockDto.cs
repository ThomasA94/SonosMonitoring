namespace SonosMonitoring.Contracts.MediaControl;

/// <summary>Explains a persistent target block caused by an uncertain external effect.</summary>
/// <param name="OperationId">Operation that may still affect the device.</param>
/// <param name="DeviceKeys">All affected stable player identities.</param>
/// <param name="Action">Original intention.</param>
/// <param name="Value">Original numeric target, if supplied.</param>
/// <param name="Status">Whether dispatch is currently begun or its outcome remains unknown.</param>
public sealed record MediaBlockDto(Guid OperationId, IReadOnlyList<string> DeviceKeys, string Action, int? Value,
    string Status = "outcome_unknown");
