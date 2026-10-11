using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Simulates silent range hosts and one healthy Sonos description.</summary>
internal sealed class DiscoveryHandler : HttpMessageHandler
{
    private const string PLAYER_DESCRIPTION = """
        <root><device><deviceType>urn:schemas-upnp-org:device:ZonePlayer:1</deviceType><manufacturer>Sonos, Inc.</manufacturer><UDN>uuid:RINCON_ABC</UDN><roomName>Test Player</roomName><serviceList><service><serviceType>urn:schemas-upnp-org:service:ZoneGroupTopology:1</serviceType></service></serviceList></device></root>
        """;

    private int _activeRequests;
    private int _maximumConcurrentRequests;
    private readonly SemaphoreSlim _identityRequestSignal = new(0);

    /// <summary>Gets addresses requested through the identity endpoint.</summary>
    public ConcurrentQueue<string> IdentityRequests { get; } = new();

    /// <summary>Gets the largest number of overlapping HTTP requests observed by the simulator.</summary>
    public int MaximumConcurrentRequests => this._maximumConcurrentRequests;

    /// <summary>Waits until the requested number of identity requests have started.</summary>
    /// <param name="requestCount">Total identity requests expected since handler creation.</param>
    /// <param name="ct">The deterministic test's overall time bound.</param>
    public async Task WaitForIdentityRequestsAsync(int requestCount, CancellationToken ct)
    {
        while (this.IdentityRequests.Count < requestCount)
        {
            await this._identityRequestSignal.WaitAsync(ct);
        }
    }

    /// <summary>Records each request and simulates the configured network outcomes.</summary>
    /// <param name="request">Request from the bounded player transport.</param>
    /// <param name="cancellationToken">Sweep or request deadline.</param>
    /// <returns>A simulated response or cancellation for a silent host.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        int active = Interlocked.Increment(ref this._activeRequests);
        this.UpdateMaximum(active);
        try
        {
            string address = request.RequestUri!.Host;
            if (request.RequestUri.AbsolutePath == "/xml/device_description.xml")
            {
                this.IdentityRequests.Enqueue(address);
                this._identityRequestSignal.Release();
                if (address != "192.168.50.6")
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(PLAYER_DESCRIPTION, Encoding.UTF8, "text/xml")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
        finally
        {
            Interlocked.Decrement(ref this._activeRequests);
        }
    }

    /// <summary>Retains the greatest observed overlap using an atomic update.</summary>
    /// <param name="active">Current in-flight request count.</param>
    private void UpdateMaximum(int active)
    {
        int observed;
        do
        {
            observed = this._maximumConcurrentRequests;
            if (active <= observed)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref this._maximumConcurrentRequests, active, observed) != observed);
    }

    /// <summary>Releases the local signal used by deterministic request-start waits.</summary>
    /// <param name="disposing">Whether managed resources should be disposed.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this._identityRequestSignal.Dispose();
        }

        base.Dispose(disposing);
    }
}
