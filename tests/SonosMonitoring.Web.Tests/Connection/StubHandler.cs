namespace SonosMonitoring.Web.Tests.Connection;

/// <summary>Supplies deterministic transport responses without contacting a shared backend.</summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handle;

    /// <summary>Captures a per-test response function.</summary>
    /// <param name="handle">Function observing each request.</param>
    public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) => this._handle = handle;

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => this._handle(request);
}
