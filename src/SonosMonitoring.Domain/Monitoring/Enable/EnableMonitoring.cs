namespace SonosMonitoring.Domain.Monitoring.Enable;

/// <summary>Expresses the operator's intent to enable monitoring for one device.</summary>
public sealed class EnableMonitoring : Command<DeviceMonitoringId>
{
    /// <summary>Creates a retryable policy action with an explicit optimistic concurrency precondition.</summary>
    /// <param name="id">Stable target stream identity.</param>
    /// <param name="requestId">Stable identity reused for retries of this exact action.</param>
    /// <param name="deviceKey">Stable device UDN; never an IP address.</param>
    /// <param name="intervalSeconds">Polling interval between 5 and 300 seconds.</param>
    /// <param name="expected">Previous commit token; null requires a new stream.</param>
    public EnableMonitoring(DeviceMonitoringId id, IRequestId requestId, string deviceKey, int intervalSeconds, AggregateConcurrencyToken? expected = null)
        : base(1, id, requestId, expected)
    {
        this.DeviceKey = deviceKey;
        this.IntervalSeconds = intervalSeconds;
    }

    /// <summary>Gets the device identity supplied by the operator.</summary>
    public string DeviceKey { get; }
    /// <summary>Gets the desired interval, in seconds.</summary>
    public int IntervalSeconds { get; }
}
