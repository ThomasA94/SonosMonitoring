using System.Globalization;

namespace SonosMonitoring.Infrastructure.Collection.Gena;

/// <summary>Manages only the supported topology subscription on allowlisted literal player addresses.</summary>
public sealed class GenaTransport : IDisposable
{
    private readonly HttpClient _client;
    private readonly Ipv4TargetPolicy _policy;
    private readonly CollectorOptions _options;

    /// <summary>Creates a dedicated bounded subscription connection pool without proxy or redirects.</summary>
    /// <param name="policy">Explicit device target boundaries.</param>
    /// <param name="options">Request and lease limits.</param>
    public GenaTransport(Ipv4TargetPolicy policy, CollectorOptions options)
    {
        this._policy = policy;
        this._options = options;
        this._client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            MaxConnectionsPerServer = 1,
            MaxResponseHeadersLength = 8,
            ConnectTimeout = options.RequestTimeout
        })
        { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>Creates or renews a subscription; renewal never sends CALLBACK or NT.</summary>
    /// <param name="address">Previously validated player address.</param>
    /// <param name="callback">Configured callback URL with an opaque per-lease path.</param>
    /// <param name="sid">Current SID for renewal, or null for creation.</param>
    /// <param name="cancellationToken">Worker cancellation.</param>
    public async Task<GenaLeaseGrant> SubscribeAsync(string address, Uri callback, string? sid, CancellationToken cancellationToken)
    {
        using var request = this.CreateRequest("SUBSCRIBE", address, sid);
        request.Headers.Add("TIMEOUT", "Second-" + this._options.Gena.RequestedLeaseSeconds.ToString(CultureInfo.InvariantCulture));
        if (sid is null)
        {
            request.Headers.Add("NT", "upnp:event");
            request.Headers.Add("CALLBACK", "<" + callback.AbsoluteUri + ">");
        }
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(this._options.RequestTimeout);
        using var response = await this._client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
        response.EnsureSuccessStatusCode();
        var grant = GenaLeaseGrant.Parse(Header(response, "SID"), Header(response, "TIMEOUT"));
        if (sid is not null && grant.Sid != sid)
        {
            throw new FormatException("Renewal changed the subscription identity.");
        }
        return grant;
    }

    /// <summary>Releases a known lease; expiry remains the fallback when shutdown cannot reach the player.</summary>
    /// <param name="address">Original approved address.</param>
    /// <param name="sid">Known subscription identifier.</param>
    /// <param name="cancellationToken">Bounded shutdown or removal budget.</param>
    public async Task UnsubscribeAsync(string address, string sid, CancellationToken cancellationToken)
    {
        using var request = this.CreateRequest("UNSUBSCRIBE", address, sid);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(this._options.RequestTimeout);
        using var response = await this._client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
    }

    /// <summary>Builds a fixed-path request after checking the literal target.</summary>
    /// <param name="method">Owned subscription method.</param>
    /// <param name="address">Literal player address.</param>
    /// <param name="sid">Optional validated subscription identifier.</param>
    private HttpRequestMessage CreateRequest(string method, string address, string? sid)
    {
        if (this._policy.IsAllowed(address) == false)
        {
            throw new InvalidOperationException("Subscription target is outside allowed ranges.");
        }
        var request = new HttpRequestMessage(new HttpMethod(method), $"http://{address}:1400/ZoneGroupTopology/Event");
        if (sid is not null)
        {
            request.Headers.Add("SID", sid);
        }
        return request;
    }

    /// <summary>Accepts exactly one occurrence of a response contract header.</summary>
    /// <param name="response">Subscription response.</param>
    /// <param name="name">Required header name.</param>
    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) && values.Count() == 1 ? values.Single() : null;

    /// <summary>Disposes the owned transport pool.</summary>
    public void Dispose() => this._client.Dispose();
}
