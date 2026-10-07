using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace SonosMonitoring.E2E.Tests.Collection.Gena;

/// <summary>Simulates one Sonos topology event publisher on a dedicated loopback alias.</summary>
internal sealed class GenaLoopbackSimulator : IAsyncDisposable
{
    private const string PLAYER_ADDRESS = "127.0.0.6";
    private const string SERVICE_NAMESPACE = "urn:schemas-upnp-org:service:ZoneGroupTopology:1";
    private readonly WebApplication _app;
    private readonly HttpClient _callbackClient;
    private readonly ConcurrentQueue<string> _createdSids = new();
    private readonly ConcurrentQueue<string> _renewedSids = new();
    private readonly ConcurrentQueue<bool> _renewalContracts = new();
    private int _nextSid;
    private int _dropInitialCount;
    private int _unsubscribeCount;
    private int _topologyCount;
    private int _initialResponseRaceCount;
    private int _rejectRenewal;
    private readonly ConcurrentQueue<HttpStatusCode> _notificationStatuses = new();

    /// <summary>Builds an isolated HTTP publisher and a callback client sourced from its player address.</summary>
    public GenaLoopbackSimulator()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration["AllowedHosts"] = PLAYER_ADDRESS;
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://{PLAYER_ADDRESS}:1400");
        this._app = builder.Build();
        this._app.MapMethods("/ZoneGroupTopology/Event", ["SUBSCRIBE", "UNSUBSCRIBE"], this.EventAsync);
        this._app.MapPost("/ZoneGroupTopology/Control", this.TopologyAsync);

        SocketsHttpHandler handler = new()
        {
            UseProxy = false,
            ConnectCallback = this.ConnectFromPlayerAsync
        };
        this._callbackClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
    }

    /// <summary>Gets how many new leases were requested.</summary>
    public int CreatedLeaseCount => this._createdSids.Count;

    /// <summary>Gets the identifiers returned for new subscriptions.</summary>
    public IReadOnlyList<string> CreatedSids => this._createdSids.ToArray();

    /// <summary>Gets how many renewals were requested.</summary>
    public int RenewalCount => this._renewedSids.Count;

    /// <summary>Gets the identifiers presented for renewal.</summary>
    public IReadOnlyList<string> RenewedSids => this._renewedSids.ToArray();

    /// <summary>Gets whether each renewal omitted initial-only headers and included a lease request.</summary>
    public bool RenewalContractValid => this._renewalContracts.Count > 0 && this._renewalContracts.All(valid => valid);

    /// <summary>Gets how many cancellations were requested.</summary>
    public int UnsubscribeCount => Volatile.Read(ref this._unsubscribeCount);

    /// <summary>Gets how many topology reconciliation actions were requested.</summary>
    public int TopologyCount => Volatile.Read(ref this._topologyCount);

    /// <summary>Gets whether the initial callback was sent while its SUBSCRIBE response remained in flight.</summary>
    public int InitialResponseRaceCount => Volatile.Read(ref this._initialResponseRaceCount);

    /// <summary>Gets callback responses in the order they were observed by the publisher.</summary>
    public IReadOnlyList<HttpStatusCode> NotificationStatuses => this._notificationStatuses.ToArray();

    /// <summary>Suppresses the requested number of initial callbacks before a later lease recovers.</summary>
    /// <param name="count">Number of initial callbacks to omit.</param>
    public void DropNextInitialCallbacks(int count) => Volatile.Write(ref this._dropInitialCount, count);

    /// <summary>Simulates a publisher that has lost the next renewed lease.</summary>
    public void RejectNextRenewal() => Volatile.Write(ref this._rejectRenewal, 1);

    /// <summary>Attempts to reuse a known callback route from an address other than the publisher.</summary>
    /// <param name="sid">The otherwise valid lease identity.</param>
    public async Task<HttpStatusCode> NotifyFromForeignPeerAsync(string sid)
    {
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
        using var request = new HttpRequestMessage(new HttpMethod("NOTIFY"), this._callbacks.Last())
        {
            Content = new StringContent(TopologyEventXml(), Encoding.UTF8, "text/xml")
        };
        request.Headers.Add("SID", sid);
        request.Headers.Add("NT", "upnp:event");
        request.Headers.Add("NTS", "upnp:propchange");
        request.Headers.Add("SEQ", "1");
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>Starts the fake player endpoint.</summary>
    public Task StartAsync() => this._app.StartAsync();

    /// <summary>Sends a controlled event to a callback URL captured from SUBSCRIBE.</summary>
    /// <param name="sid">Subscription identifier for the event.</param>
    /// <param name="sequence">UPnP event sequence header value.</param>
    /// <param name="xml">Event property set body.</param>
    /// <param name="nt">Optional NT header override for malformed request scenarios.</param>
    public async Task<HttpStatusCode> NotifyAsync(string sid, string sequence, string xml, string nt = "upnp:event")
    {
        string callback = this._callbacks.TryPeek(out string? captured) ? captured :
            throw new InvalidOperationException("No initial subscription callback has been captured.");
        return await this.SendNotificationAsync(callback, sid, sequence, xml, nt);
    }

    /// <summary>Stops publisher/listener resources and releases the callback connection pool.</summary>
    public async ValueTask DisposeAsync()
    {
        await this._app.StopAsync();
        await this._app.DisposeAsync();
        this._callbackClient.Dispose();
    }

    private readonly ConcurrentQueue<string> _callbacks = new();

    /// <summary>Handles initial subscriptions, renewals, and unsubscribe requests.</summary>
    /// <param name="context">Incoming publisher request.</param>
    private async Task EventAsync(HttpContext context)
    {
        if (context.Request.Method == "UNSUBSCRIBE")
        {
            Interlocked.Increment(ref this._unsubscribeCount);
            context.Response.StatusCode = StatusCodes.Status200OK;
            return;
        }

        string? requestedSid = context.Request.Headers["SID"].FirstOrDefault();
        if (requestedSid is not null && Interlocked.Exchange(ref this._rejectRenewal, 0) == 1)
        {
            context.Response.StatusCode = 412;
            return;
        }
        bool isCreation = string.IsNullOrEmpty(requestedSid);
        string sid = isCreation ? $"uuid:sim-{Interlocked.Increment(ref this._nextSid)}" : requestedSid!;
        if (isCreation)
        {
            this._createdSids.Enqueue(sid);
            string callback = context.Request.Headers["CALLBACK"].ToString().Trim('<', '>');
            this._callbacks.Enqueue(callback);
        }
        else
        {
            this._renewedSids.Enqueue(sid);
            this._renewalContracts.Enqueue(
                context.Request.Headers.ContainsKey("CALLBACK") == false &&
                context.Request.Headers.ContainsKey("NT") == false &&
                context.Request.Headers["TIMEOUT"].ToString().StartsWith("Second-", StringComparison.Ordinal));
        }

        context.Response.Headers["SID"] = sid;
        context.Response.Headers["TIMEOUT"] = "Second-6";
        context.Response.StatusCode = StatusCodes.Status200OK;

        if (isCreation && Interlocked.CompareExchange(ref this._dropInitialCount, 0, 0) == 0)
        {
            // Finish NOTIFY before sending response headers, forcing provisional SID handling.
            string callback = this._callbacks.Last();
            HttpStatusCode status = await this.SendNotificationAsync(callback, sid, "0", TopologyEventXml(), "upnp:event");
            this._notificationStatuses.Enqueue(status);
            Interlocked.Increment(ref this._initialResponseRaceCount);
        }
        else if (isCreation)
        {
            Interlocked.Decrement(ref this._dropInitialCount);
        }
        await context.Response.StartAsync(context.RequestAborted);
    }

    /// <summary>Sends one topology SOAP response and counts reconciliations.</summary>
    /// <param name="context">Incoming topology action.</param>
    private async Task TopologyAsync(HttpContext context)
    {
        Interlocked.Increment(ref this._topologyCount);
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace service = SERVICE_NAMESPACE;
        XElement response = new(soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "s", soap),
            new XElement(soap + "Body",
                new XElement(service + "GetZoneGroupStateResponse",
                    new XElement("ZoneGroupState", $"<ZoneGroups><ZoneGroup ID=\"g\" Coordinator=\"{GenaWorkerHarness.DEVICE_ID}\"><ZoneGroupMember UUID=\"{GenaWorkerHarness.DEVICE_ID}\" /></ZoneGroup></ZoneGroups>"))));
        context.Response.ContentType = "text/xml";
        await context.Response.WriteAsync(response.ToString(SaveOptions.DisableFormatting), context.RequestAborted);
    }

    /// <summary>Posts one standards-shaped NOTIFY using the simulated player's source address.</summary>
    /// <param name="callback">Callback URL obtained from the player's request.</param>
    /// <param name="sid">Subscription identifier.</param>
    /// <param name="sequence">UPnP sequence text.</param>
    /// <param name="xml">Propertyset body.</param>
    /// <param name="nt">Notification type override.</param>
    /// <returns>The callback response status code.</returns>
    private async Task<HttpStatusCode> SendNotificationAsync(string callback, string sid, string sequence, string xml, string nt)
    {
        using HttpRequestMessage request = new(new HttpMethod("NOTIFY"), callback)
        {
            Content = new StringContent(xml, Encoding.UTF8, "text/xml")
        };
        request.Headers.Add("NT", nt);
        request.Headers.Add("NTS", "upnp:propchange");
        request.Headers.Add("SID", sid);
        request.Headers.Add("SEQ", sequence);
        // Observe header-level rejection before uploading a deliberately oversized body.
        request.Headers.ExpectContinue = true;
        using HttpResponseMessage response = await this._callbackClient.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>Binds callback connections to the simulator address so the worker can verify the peer.</summary>
    /// <param name="context">Requested callback destination.</param>
    /// <param name="cancellationToken">Socket connection cancellation.</param>
    /// <returns>The connected callback socket stream.</returns>
    private async ValueTask<Stream> ConnectFromPlayerAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Parse(PLAYER_ADDRESS), 0));
        try
        {
            await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Builds a valid UPnP event property set containing a topology hint.</summary>
    /// <returns>XML body accepted by the worker parser.</returns>
    private static string TopologyEventXml() =>
        "<e:propertyset xmlns:e=\"urn:schemas-upnp-org:event-1-0\"><e:property><ZoneGroupState>hint</ZoneGroupState></e:property></e:propertyset>";
}
