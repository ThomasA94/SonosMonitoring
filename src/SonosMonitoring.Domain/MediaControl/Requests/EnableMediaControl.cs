namespace SonosMonitoring.Domain.MediaControl.Requests;

/// <summary>Requests the enable-control installation decision.</summary>
public sealed class EnableMediaControl : RequestMediaControl
{
    /// <summary>Captures this decision before its coordinated effect is committed.</summary>
    /// <param name="id">Stable operation stream identity.</param>
    /// <param name="requestId">Stable request identity for retries.</param>
    /// <param name="payloadJson">Sanitized immutable decision context.</param>
    /// <param name="fingerprint">Hash of the complete decision.</param>
    /// <param name="generation">Current grant generation.</param>
    /// <param name="acceptedAtUtc">Acceptance time.</param>
    /// <param name="expiresAtUtc">Decision deadline.</param>
    /// <param name="inputs">Decision-specific structured inputs.</param>
    public EnableMediaControl(MediaOperationId id, IRequestId requestId, string payloadJson,
        string fingerprint, string generation, DateTimeOffset acceptedAtUtc,
        DateTimeOffset expiresAtUtc, MediaIntentInputs inputs)
        : base(id, requestId, "enable-control", "installation", payloadJson, fingerprint, generation,
            acceptedAtUtc, expiresAtUtc, inputs)
    { }
}
