namespace SonosMonitoring.Infrastructure.MediaControl.Transport;

/// <summary>Reports a bounded UPnP SOAP fault code without exposing device-provided text.</summary>
public sealed class MediaSoapFaultException : Exception
{
    /// <summary>Creates a safe fault report.</summary>
    /// <param name="code">Bounded device fault code.</param>
    public MediaSoapFaultException(string code) : base($"Sonos SOAP fault {code}.")
    {
        this.Code = code;
    }

    /// <summary>Gets the bounded device fault code.</summary>
    public string Code { get; }
}
