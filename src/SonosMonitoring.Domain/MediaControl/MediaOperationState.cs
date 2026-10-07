namespace SonosMonitoring.Domain.MediaControl;

/// <summary>Immutable persisted operation state; it never claims to be the player state.</summary>
public sealed class MediaOperationState : Entity<MediaOperationId>
{
    /// <summary>Constructs an operation state for event replay and snapshots.</summary>
    /// <param name="id">Operation stream identity.</param>
    /// <param name="action">Named user intention.</param>
    /// <param name="deviceKey">Stable selected device key.</param>
    /// <param name="payloadJson">Sanitized immutable context descriptor.</param>
    /// <param name="fingerprint">Hash of the complete request context.</param>
    /// <param name="generation">Grant generation captured at acceptance.</param>
    /// <param name="acceptedAtUtc">Acceptance time.</param>
    /// <param name="expiresAtUtc">Deadline for starting dispatch.</param>
    /// <param name="status">Current durable lifecycle status.</param>
    /// <param name="step">Last begun writing step.</param>
    /// <param name="outcome">Recorded result, when present.</param>
    /// <param name="resultCode">Safe machine-readable diagnostic code.</param>
    public MediaOperationState(MediaOperationId id, string action = "", string deviceKey = "", string payloadJson = "",
        string fingerprint = "", string generation = "", DateTimeOffset acceptedAtUtc = default,
        DateTimeOffset expiresAtUtc = default, string status = "", int step = -1,
        string outcome = "", string resultCode = "") : base(id)
    {
        this.Action = action;
        this.DeviceKey = deviceKey;
        this.PayloadJson = payloadJson;
        this.Fingerprint = fingerprint;
        this.Generation = generation;
        this.AcceptedAtUtc = acceptedAtUtc;
        this.ExpiresAtUtc = expiresAtUtc;
        this.Status = status;
        this.Step = step;
        this.Outcome = outcome;
        this.ResultCode = resultCode;
    }

    /// <summary>Gets the named user intention.</summary>
    public string Action { get; }
    /// <summary>Gets the selected device identity.</summary>
    public string DeviceKey { get; }
    /// <summary>Gets the sanitized execution context.</summary>
    public string PayloadJson { get; }
    /// <summary>Gets the immutable request fingerprint.</summary>
    public string Fingerprint { get; }
    /// <summary>Gets the captured grant generation.</summary>
    public string Generation { get; }
    /// <summary>Gets the acceptance time.</summary>
    public DateTimeOffset AcceptedAtUtc { get; }
    /// <summary>Gets the dispatch deadline.</summary>
    public DateTimeOffset ExpiresAtUtc { get; }
    /// <summary>Gets the lifecycle status.</summary>
    public string Status { get; }
    /// <summary>Gets the most recently begun writing step.</summary>
    public int Step { get; }
    /// <summary>Gets the result classification.</summary>
    public string Outcome { get; }
    /// <summary>Gets a safe diagnostic code.</summary>
    public string ResultCode { get; }
}
