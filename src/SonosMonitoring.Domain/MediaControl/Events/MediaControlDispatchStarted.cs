namespace SonosMonitoring.Domain.MediaControl.Events;

/// <summary>Records the complete resulting media operation state for replay and projections.</summary>
[EventContract("sonosmonitoring.media_control.dispatch_started", 1)]
public sealed class MediaControlDispatchStarted : AggregateEvent<MediaControlDispatchStarted, MediaControlOperation, MediaOperationId>
{
    /// <summary>Gets the named user intention.</summary>
    public string Action { get; init; } = string.Empty;
    /// <summary>Gets the selected device key.</summary>
    public string DeviceKey { get; init; } = string.Empty;
    /// <summary>Gets the sanitized immutable context descriptor.</summary>
    public string PayloadJson { get; init; } = string.Empty;
    /// <summary>Gets the immutable request fingerprint.</summary>
    public string Fingerprint { get; init; } = string.Empty;
    /// <summary>Gets the captured grant generation.</summary>
    public string Generation { get; init; } = string.Empty;
    /// <summary>Gets the original acceptance time.</summary>
    public DateTimeOffset AcceptedAtUtc { get; init; }
    /// <summary>Gets the last possible dispatch start time.</summary>
    public DateTimeOffset ExpiresAtUtc { get; init; }
    /// <summary>Gets the lifecycle status after this fact.</summary>
    public string Status { get; init; } = string.Empty;
    /// <summary>Gets the writing step last begun, or minus one before dispatch.</summary>
    public int Step { get; init; } = -1;
    /// <summary>Gets the recorded result classification.</summary>
    public string Outcome { get; init; } = string.Empty;
    /// <summary>Gets a safe machine-readable diagnostic code.</summary>
    public string ResultCode { get; init; } = string.Empty;
}
