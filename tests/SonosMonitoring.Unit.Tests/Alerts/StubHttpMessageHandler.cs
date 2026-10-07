namespace SonosMonitoring.Unit.Tests.Alerts;

/// <summary>Supplies deterministic HTTP responses to alert reader tests.</summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    /// <summary>Creates a handler with the supplied response behavior.</summary>
    /// <param name="respond">Function that receives each request and returns its response.</param>
    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
        this._respond = respond;

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        this._respond(request, cancellationToken);
}
