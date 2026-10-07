using SonosMonitoring.Contracts.MediaControl;
using SonosMonitoring.Web.Connection;

namespace SonosMonitoring.Web.MediaControl;

/// <summary>Retains the exact action, context and request identity after an ambiguous response.</summary>
public sealed class MediaAction
{
    /// <summary>Creates one immutable named media intention from the displayed player state.</summary>
    /// <param name="deviceKey">Stable selected player identity.</param>
    /// <param name="action">Named API intention.</param>
    /// <param name="request">Original request body, including topology and source preconditions.</param>
    public MediaAction(string deviceKey, string action, MediaActionRequest request)
    {
        this.DeviceKey = deviceKey;
        this.Action = action;
        this.Request = request;
        this.OperationId = Guid.NewGuid();
    }

    /// <summary>Gets the stable operation and HTTP idempotency identity.</summary>
    public Guid OperationId { get; }

    /// <summary>Gets the original selected player identity.</summary>
    public string DeviceKey { get; }

    /// <summary>Gets the named intention.</summary>
    public string Action { get; }

    /// <summary>Gets the original immutable body for an explicit retry.</summary>
    public MediaActionRequest Request { get; }

    /// <summary>Sends the original operation once; only the caller may choose an explicit retry.</summary>
    /// <param name="api">Authenticated API-only transport.</param>
    /// <param name="ct">Component lifetime cancellation.</param>
    /// <returns>Accepted operation or safe error.</returns>
    public Task<ApiResult<MediaOperationDto>> SendAsync(MonitoringApiClient api, CancellationToken ct) =>
        api.PostAsync<MediaOperationDto>("api/media/players/" + Uri.EscapeDataString(this.DeviceKey) + "/" + this.Action,
            this.Request, this.OperationId, ct);
}
