namespace SonosMonitoring.Domain.MediaControl.Requests;

/// <summary>Requests the mode media intention for one selected device.</summary>
public sealed class ChoosePlaybackMode : RequestMediaControl
{
    /// <summary>Captures this named intention and its immutable execution context.</summary>
    /// <param name="id">Stable operation stream identity.</param>
    /// <param name="requestId">Stable request identity for retries.</param>
    /// <param name="deviceKey">Selected device identity.</param>
    /// <param name="payloadJson">Sanitized execution context.</param>
    /// <param name="fingerprint">Hash of the complete intention.</param>
    /// <param name="generation">Captured grant generation.</param>
    /// <param name="acceptedAtUtc">Acceptance time.</param>
    /// <param name="expiresAtUtc">Dispatch deadline.</param>
    /// <param name="inputs">Action-specific structured inputs.</param>
    public ChoosePlaybackMode(MediaOperationId id, IRequestId requestId, string deviceKey, string payloadJson,
        string fingerprint, string generation, DateTimeOffset acceptedAtUtc,
        DateTimeOffset expiresAtUtc, MediaIntentInputs inputs)
        : base(id, requestId, "mode", deviceKey, payloadJson, fingerprint, generation,
            acceptedAtUtc, expiresAtUtc, inputs)
    { }
}
