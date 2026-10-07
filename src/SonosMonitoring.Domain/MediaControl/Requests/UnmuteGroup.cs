namespace SonosMonitoring.Domain.MediaControl.Requests;

/// <summary>Requests the group-unmute media intention for one selected device.</summary>
public sealed class UnmuteGroup : RequestMediaControl
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
    public UnmuteGroup(MediaOperationId id, IRequestId requestId, string deviceKey, string payloadJson,
        string fingerprint, string generation, DateTimeOffset acceptedAtUtc,
        DateTimeOffset expiresAtUtc, MediaIntentInputs inputs)
        : base(id, requestId, "group-unmute", deviceKey, payloadJson, fingerprint, generation,
            acceptedAtUtc, expiresAtUtc, inputs)
    { }
}
