using System.Net;
using SonosMonitoring.Infrastructure.Collection;
using SonosMonitoring.Infrastructure.MediaControl.Transport;

namespace SonosMonitoring.Unit.Tests.MediaControl.Transport;

/// <summary>Exercises the local transport boundary with fake HTTP instead of real speakers.</summary>
public sealed class MediaSoapClientTests
{
    /// <summary>Rejects addresses outside current CIDRs before an HTTP message is issued.</summary>
    [Fact]
    public async Task RejectsUnapprovedTargetBeforeSending()
    {
        int calls = 0;
        using MediaSoapClient client = new(() => new CollectorOptions { Enabled = true, AllowedCidrs = ["192.168.1.0/24"] },
            new FakeHandler(_ => { calls++; return new HttpResponseMessage(HttpStatusCode.OK); }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CallAsync("192.168.2.2", "AVTransport", "Play",
            new Dictionary<string, string> { ["InstanceID"] = "0", ["Speed"] = "1" }, CancellationToken.None));
        Assert.Equal(0, calls);
    }

    /// <summary>Preserves the exact SOAP rejection despite HTTP 500 and sends only once.</summary>
    [Fact]
    public async Task ReportsHttp500SoapFaultAfterOneAttempt()
    {
        int calls = 0;
        using MediaSoapClient client = new(() => new CollectorOptions
        {
            Enabled = true,
            AllowedCidrs = ["127.0.0.0/8"],
            AllowLoopback = true,
            RequestTimeout = TimeSpan.FromSeconds(1)
        },
            new FakeHandler(request =>
            {
                calls++;
                Assert.Equal(new Uri("http://127.0.0.1:1400/MediaRenderer/AVTransport/Control"), request.RequestUri);
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><detail><UPnPError><errorCode>701</errorCode></UPnPError></detail></s:Fault></s:Body></s:Envelope>")
                };
            }));
        MediaSoapFaultException error = await Assert.ThrowsAsync<MediaSoapFaultException>(() => client.CallAsync("127.0.0.1",
            "AVTransport", "Play", new Dictionary<string, string> { ["InstanceID"] = "0", ["Speed"] = "1" }, CancellationToken.None));
        Assert.Equal("701", error.Code);
        Assert.Equal(1, calls);
    }

    /// <summary>A device-supplied redirect cannot move a request to an unapproved host.</summary>
    [Fact]
    public async Task RedirectIsRejectedWithoutFollowingIt()
    {
        int calls = 0;
        using MediaSoapClient client = new(() => new CollectorOptions
        {
            Enabled = true,
            AllowedCidrs = ["127.0.0.0/8"],
            AllowLoopback = true,
            RequestTimeout = TimeSpan.FromSeconds(1)
        }, new FakeHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("http://example.invalid/escape") }
            };
        }));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.DescriptionAsync("127.0.0.1", CancellationToken.None));
        Assert.Equal(1, calls);
    }

    /// <summary>Responds without I/O to a fake request.</summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        /// <summary>Returns the configured fake response.</summary>
        /// <param name="request">Captured HTTP message.</param>
        /// <param name="cancellationToken">Unused fake cancellation.</param>
        /// <returns>Configured response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}
