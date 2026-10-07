namespace SonosMonitoring.Application.MediaControl.Transport;

/// <summary>Provides validated local Sonos observations and single-attempt write steps.</summary>
public interface IMediaDeviceGateway
{
    /// <summary>Reads a player and its current group context.</summary>
    /// <param name="deviceKey">Selected stable player UDN.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<MediaPlayback> ReadAsync(string deviceKey, CancellationToken cancellationToken);

    /// <summary>Browses a bounded page of queue or favorite objects.</summary>
    /// <param name="deviceKey">Selected stable player UDN.</param>
    /// <param name="kind">Either queue or favorites.</param>
    /// <param name="offset">Zero-based offset.</param>
    /// <param name="limit">Page size.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<MediaPage> BrowseAsync(string deviceKey, string kind, int offset, int limit, CancellationToken cancellationToken);

    /// <summary>Validates current state and prepares immutable SOAP steps without sending writes.</summary>
    /// <param name="deviceKey">Selected stable player UDN.</param>
    /// <param name="action">Whitelisted named intention.</param>
    /// <param name="context">Displayed target and source preconditions.</param>
    /// <param name="inputs">Action values.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<IReadOnlyList<MediaWriteStep>> PrepareAsync(string deviceKey, string action, MediaTargetContext context,
        MediaWriteInputs inputs, CancellationToken cancellationToken);

    /// <summary>Sends exactly one validated SOAP step after external durable dispatch permission.</summary>
    /// <param name="step">Prepared step.</param>
    /// <param name="cancellationToken">Dispatch cancellation.</param>
    Task SendAsync(MediaWriteStep step, CancellationToken cancellationToken);

    /// <summary>Compares a fresh observation with an observable desired state.</summary>
    /// <param name="action">Named intention.</param>
    /// <param name="context">Original target context.</param>
    /// <param name="inputs">Original values.</param>
    /// <param name="cancellationToken">Read cancellation.</param>
    /// <returns>Match or mismatch; null when no reliable desired state is observable.</returns>
    Task<bool?> ConfirmAsync(string action, MediaTargetContext context, MediaWriteInputs inputs,
        CancellationToken cancellationToken);
}
