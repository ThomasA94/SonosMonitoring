namespace SonosMonitoring.Domain.MediaControl;

/// <summary>Identifies one durable media action across request retries.</summary>
public sealed class MediaOperationId : AggregateIdentity<MediaOperationId>
{
    /// <summary>Restores a canonical persisted identity.</summary>
    /// <param name="value">Canonical identifier.</param>
    public MediaOperationId(string value) : base(value) { }
}
