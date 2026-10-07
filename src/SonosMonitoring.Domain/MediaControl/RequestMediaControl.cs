namespace SonosMonitoring.Domain.MediaControl;

/// <summary>Common immutable envelope for one specifically named user intention.</summary>
public abstract class RequestMediaControl : Command<MediaOperationId>
{
    /// <summary>Captures request facts before any external write is considered.</summary>
    /// <param name="id">Operation stream identity.</param>
    /// <param name="requestId">Stable request identity for retries.</param>
    /// <param name="action">Named intention.</param>
    /// <param name="deviceKey">Stable selected device.</param>
    /// <param name="payloadJson">Sanitized execution context.</param>
    /// <param name="fingerprint">Hash of the complete immutable intention.</param>
    /// <param name="generation">Captured grant generation.</param>
    /// <param name="acceptedAtUtc">Acceptance time.</param>
    /// <param name="expiresAtUtc">Dispatch deadline.</param>
    /// <param name="inputs">Action-specific structured inputs.</param>
    protected RequestMediaControl(MediaOperationId id, IRequestId requestId, string action, string deviceKey,
        string payloadJson, string fingerprint, string generation, DateTimeOffset acceptedAtUtc,
        DateTimeOffset expiresAtUtc, MediaIntentInputs inputs) : base(1, id, requestId)
    {
        this.Action = action;
        this.DeviceKey = deviceKey;
        this.PayloadJson = payloadJson;
        this.Fingerprint = fingerprint;
        this.Generation = generation;
        this.AcceptedAtUtc = acceptedAtUtc;
        this.ExpiresAtUtc = expiresAtUtc;
        this.Inputs = inputs;
    }

    /// <summary>Gets the named intention.</summary>
    public string Action { get; }
    /// <summary>Gets the selected device identity.</summary>
    public string DeviceKey { get; }
    /// <summary>Gets the sanitized execution context.</summary>
    public string PayloadJson { get; }
    /// <summary>Gets the request fingerprint.</summary>
    public string Fingerprint { get; }
    /// <summary>Gets the captured grant generation.</summary>
    public string Generation { get; }
    /// <summary>Gets the acceptance time.</summary>
    public DateTimeOffset AcceptedAtUtc { get; }
    /// <summary>Gets the dispatch deadline.</summary>
    public DateTimeOffset ExpiresAtUtc { get; }
    /// <summary>Gets action-specific structured inputs.</summary>
    public MediaIntentInputs Inputs { get; }
}
