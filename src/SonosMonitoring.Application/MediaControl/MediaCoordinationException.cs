namespace SonosMonitoring.Application.MediaControl;

/// <summary>Reports a safe, stable application rejection without leaking device or persistence payloads.</summary>
public sealed class MediaCoordinationException : Exception
{
    /// <summary>Constructs the rejection.</summary>
    /// <param name="code">Public error code.</param>
    /// <param name="statusCode">HTTP status for the transport boundary.</param>
    public MediaCoordinationException(string code, int statusCode = 409) : base(code)
    {
        this.Code = code;
        this.StatusCode = statusCode;
    }

    /// <summary>Gets the safe error code.</summary>
    public string Code { get; }
    /// <summary>Gets the transport status.</summary>
    public int StatusCode { get; }
}
