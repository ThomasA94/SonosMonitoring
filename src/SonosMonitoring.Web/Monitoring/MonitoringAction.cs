using SonosMonitoring.Contracts.Monitoring;
using SonosMonitoring.Web.Connection;

namespace SonosMonitoring.Web.Monitoring;

/// <summary>Retains an immutable command identity and payload across ambiguous HTTP retries.</summary>
public sealed class MonitoringAction
{
    /// <summary>Captures one explicit user intention.</summary>
    /// <param name="deviceKey">Selected device identity.</param>
    /// <param name="enable">Whether to activate rather than deactivate collection.</param>
    /// <param name="interval">Requested activation interval.</param>
    /// <param name="token">Opaque token of the state reviewed by the user.</param>
    public MonitoringAction(string deviceKey, bool enable, int interval, string? token)
    {
        this.Path = "api/monitoring/" + Uri.EscapeDataString(deviceKey) + (enable ? "/enable" : "/disable");
        this.Body = enable ? new EnableMonitoringRequest(interval, token) : new DisableMonitoringRequest(token ?? "");
    }

    /// <summary>Gets the identity reused for every retry of this exact intention.</summary>
    public Guid RequestId { get; } = Guid.NewGuid();
    /// <summary>Gets the captured route.</summary>
    public string Path { get; }
    /// <summary>Gets the immutable captured transport body.</summary>
    public object Body { get; }

    /// <summary>Retries only when the user explicitly requests the same action.</summary>
    /// <param name="api">Backend transport.</param>
    /// <param name="ct">Component lifetime cancellation.</param>
    public Task<ApiResult<MonitoringAccepted>> SendAsync(MonitoringApiClient api, CancellationToken ct) =>
        api.PostAsync<MonitoringAccepted>(this.Path, this.Body, this.RequestId, ct);
}
