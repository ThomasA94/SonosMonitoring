using SonosMonitoring.Application.MediaControl.Transport;

namespace SonosMonitoring.Application.MediaControl;

/// <summary>Keeps the originally reviewed context and action-specific fields immutable across dispatch and retries.</summary>
/// <param name="Action">Named intention.</param>
/// <param name="DeviceKey">Selected stable player.</param>
/// <param name="Context">Originally reviewed topology and source.</param>
/// <param name="Inputs">Structured intention inputs.</param>
/// <param name="TargetEpochs">Server-captured risk decision epochs retained with the original intention.</param>
public sealed record MediaExecutionPayload(string Action, string DeviceKey, MediaTargetContext Context, MediaWriteInputs Inputs,
    IReadOnlyDictionary<string, long>? TargetEpochs = null);
