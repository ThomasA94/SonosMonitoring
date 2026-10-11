using System.Net;
using System.Text;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Performs bounded read-only HTTP requests against approved literal IPv4 targets.</summary>
public sealed class PlayerProbe : IDisposable
{
    private readonly HttpClient _client;
    private readonly Ipv4TargetPolicy _policy;
    private readonly CollectorOptions _options;

    /// <summary>Creates the hardened local HTTP client.</summary>
    /// <param name="policy">Outbound target policy.</param>
    /// <param name="options">Request limits.</param>
    public PlayerProbe(Ipv4TargetPolicy policy, CollectorOptions options)
        : this(policy, options, new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            ConnectTimeout = options.RequestTimeout,
            MaxConnectionsPerServer = 2
        })
    {
    }

    /// <summary>Creates the bounded HTTP client with a supplied transport for isolated tests.</summary>
    /// <param name="policy">Outbound target policy.</param>
    /// <param name="options">Request limits.</param>
    /// <param name="handler">Owned HTTP handler that simulates device responses.</param>
    internal PlayerProbe(Ipv4TargetPolicy policy, CollectorOptions options, HttpMessageHandler handler)
    {
        this._policy = policy;
        this._options = options;
        this._client = new HttpClient(handler, true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>Reads a fixed local path only if the literal address is allowed.</summary>
    /// <param name="address">Literal IPv4 target.</param>
    /// <param name="path">Fixed application-owned path.</param>
    /// <param name="cancellationToken">Collection cancellation.</param>
    /// <returns>A bounded UTF-8 response body.</returns>
    public Task<string> GetAsync(string address, string path, CancellationToken cancellationToken)
    {
        if (this._policy.IsAllowed(address) == false ||
            path is not ("/xml/device_description.xml" or "/status/ifconfig" or "/status/wireless" or "/status/enetports"))
        {
            throw new InvalidOperationException("Outbound target or path is not allowed.");
        }

        return this.ReadAsync(address, path, null, cancellationToken);
    }

    /// <summary>Invokes only the read-only topology action on an approved literal IPv4 address.</summary>
    /// <param name="address">Validated player's literal address.</param>
    /// <param name="cancellationToken">Sweep cancellation.</param>
    /// <returns>The bounded SOAP response, without following member locations.</returns>
    public Task<string> GetTopologyAsync(string address, CancellationToken cancellationToken)
    {
        if (this._policy.IsAllowed(address) == false)
        {
            throw new InvalidOperationException("Outbound target is not allowed.");
        }

        const string BODY = """
            <?xml version="1.0" encoding="utf-8"?>
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" s:encodingStyle="http://schemas.xmlsoap.org/soap/encoding/">
              <s:Body><u:GetZoneGroupState xmlns:u="urn:schemas-upnp-org:service:ZoneGroupTopology:1" /></s:Body>
            </s:Envelope>
            """;
        return this.ReadAsync(address, "/ZoneGroupTopology/Control", BODY, cancellationToken);
    }

    /// <summary>Reads transport state using the fixed local read action and instance zero.</summary>
    /// <param name="address">Approved literal IPv4 player.</param>
    /// <param name="cancellationToken">Collection cancellation.</param>
    public Task<string> GetTransportInfoAsync(string address, CancellationToken cancellationToken) =>
        this.ReadRendererAsync(address, "AVTransport", "GetTransportInfo", false, cancellationToken);

    /// <summary>Reads player volume on the Master channel without changing it.</summary>
    /// <param name="address">Approved literal IPv4 player.</param>
    /// <param name="cancellationToken">Collection cancellation.</param>
    public Task<string> GetVolumeAsync(string address, CancellationToken cancellationToken) =>
        this.ReadRendererAsync(address, "RenderingControl", "GetVolume", true, cancellationToken);

    /// <summary>Reads player mute on the Master channel without changing it.</summary>
    /// <param name="address">Approved literal IPv4 player.</param>
    /// <param name="cancellationToken">Collection cancellation.</param>
    public Task<string> GetMuteAsync(string address, CancellationToken cancellationToken) =>
        this.ReadRendererAsync(address, "RenderingControl", "GetMute", true, cancellationToken);

    /// <summary>Builds a fixed read-only renderer request from application-owned constants.</summary>
    /// <param name="address">Approved literal IPv4 player.</param>
    /// <param name="service">Application-owned service name.</param>
    /// <param name="action">Application-owned read action.</param>
    /// <param name="masterChannel">Whether the fixed Master channel is required.</param>
    /// <param name="cancellationToken">Collection cancellation.</param>
    private Task<string> ReadRendererAsync(string address, string service, string action, bool masterChannel, CancellationToken cancellationToken)
    {
        if (this._policy.IsAllowed(address) == false)
        {
            throw new InvalidOperationException("Outbound target is not allowed.");
        }
        string serviceType = $"urn:schemas-upnp-org:service:{service}:1";
        string channel = masterChannel ? "<Channel>Master</Channel>" : "";
        string body = $"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:{action} xmlns:u=\"{serviceType}\"><InstanceID>0</InstanceID>{channel}</u:{action}></s:Body></s:Envelope>";
        return this.ReadAsync(address, $"/MediaRenderer/{service}/Control", body, cancellationToken, serviceType + "#" + action);
    }

    /// <summary>Applies identical transport, timeout and size limits to fixed GET and SOAP reads.</summary>
    /// <param name="address">Approved literal IPv4 target.</param>
    /// <param name="path">Application-owned endpoint path.</param>
    /// <param name="soapBody">Fixed topology body, or null for GET.</param>
    /// <param name="cancellationToken">Collection cancellation.</param>
    /// <returns>A bounded UTF-8 response body.</returns>
    /// <param name="soapAction">Fixed renderer action, or null for the existing topology read.</param>
    private async Task<string> ReadAsync(string address, string path, string? soapBody, CancellationToken cancellationToken, string? soapAction = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri($"http://{address}:1400{path}"));
        if (soapBody is not null)
        {
            request.Method = HttpMethod.Post;
            request.Content = new StringContent(soapBody, Encoding.UTF8, "text/xml");
            request.Headers.Add("SOAPACTION", "\"" + (soapAction ?? "urn:schemas-upnp-org:service:ZoneGroupTopology:1#GetZoneGroupState") + "\"");
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(this._options.RequestTimeout);
        using HttpResponseMessage response = await this._client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.IsSuccessStatusCode == false)
        {
            TimeSpan? retryDelay = response.Headers.RetryAfter?.Delta;
            if (retryDelay is null && response.Headers.RetryAfter?.Date is DateTimeOffset retryAt)
            {
                retryDelay = retryAt - DateTimeOffset.UtcNow;
            }

            throw new PlayerHttpException(response.StatusCode, retryDelay);
        }
        if (response.Content.Headers.ContentLength > this._options.MaxResponseBytes)
        {
            throw new InvalidDataException("Device response exceeds the configured size limit.");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[8192];
        while (true)
        {
            int read = await stream.ReadAsync(chunk, timeout.Token);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > this._options.MaxResponseBytes)
            {
                throw new InvalidDataException("Device response exceeds the configured size limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Releases the owned HTTP connection pool.</summary>
    public void Dispose()
    {
        this._client.Dispose();
    }
}
