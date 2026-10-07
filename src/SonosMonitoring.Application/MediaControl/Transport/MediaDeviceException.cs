namespace SonosMonitoring.Application.MediaControl.Transport;

/// <summary>Safe media-device failure classification for durable execution outcomes.</summary>
public sealed class MediaDeviceException : Exception
{
    /// <summary>Creates a bounded device failure without leaking protocol payloads.</summary>
    /// <param name="code">Stable safe failure category.</param>
    /// <param name="definiteRejection">Whether the device explicitly rejected a write without applying it.</param>
    public MediaDeviceException(string code, bool definiteRejection = false) : base(code)
    {
        this.Code = code;
        this.DefiniteRejection = definiteRejection;
    }

    /// <summary>Gets the safe failure category.</summary>
    public string Code { get; }

    /// <summary>Gets whether a parsed SOAP fault explicitly rejected the operation.</summary>
    public bool DefiniteRejection { get; }
}
