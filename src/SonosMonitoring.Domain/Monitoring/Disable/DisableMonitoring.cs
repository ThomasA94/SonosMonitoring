namespace SonosMonitoring.Domain.Monitoring.Disable;

/// <summary>Expresses the operator's intent to disable monitoring for one device.</summary>
public sealed class DisableMonitoring : Command<DeviceMonitoringId>
{
    /// <summary>Creates a retryable policy action with an explicit optimistic concurrency precondition.</summary>
    /// <param name="id">Stable target stream identity.</param>
    /// <param name="requestId">Stable identity reused for retries of this exact action.</param>
    /// <param name="expected">Previous commit token; null requires a new stream.</param>
    public DisableMonitoring(DeviceMonitoringId id, IRequestId requestId, AggregateConcurrencyToken? expected = null)
        : base(1, id, requestId, expected)
    { }
}
