namespace SonosMonitoring.Application.MediaControl;

/// <summary>Reports operation projection visibility without making a device execution decision.</summary>
public interface IMediaOperationProjection
{
    /// <summary>Reads the merged operation's latest projected event position.</summary>
    /// <param name="id">Stable operation identity.</param>
    /// <param name="ct">Query cancellation.</param>
    /// <returns>The projected position, or null when the document has not arrived.</returns>
    Task<long?> SequenceAsync(Guid id, CancellationToken ct);
}
