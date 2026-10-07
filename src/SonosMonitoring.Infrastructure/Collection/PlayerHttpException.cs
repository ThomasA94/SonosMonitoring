using System.Net;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>HTTP failure with an optional bounded server-requested retry delay.</summary>
public sealed class PlayerHttpException : HttpRequestException
{
    /// <summary>Creates an HTTP failure without exposing response content.</summary>
    /// <param name="statusCode">Failed status code.</param>
    /// <param name="retryDelay">Optional Retry-After interval.</param>
    public PlayerHttpException(HttpStatusCode statusCode, TimeSpan? retryDelay)
        : base($"Player returned HTTP {(int)statusCode}.", null, statusCode)
    {
        this.RetryDelay = retryDelay;
    }

    /// <summary>Gets the server-requested retry delay, if supplied.</summary>
    public TimeSpan? RetryDelay { get; }
}
