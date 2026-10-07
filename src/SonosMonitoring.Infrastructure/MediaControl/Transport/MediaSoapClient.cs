using System.Net;
using System.Text;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Infrastructure.MediaControl.Transport;

/// <summary>Uses only literal approved IPv4 destinations on port 1400 without redirects or proxies.</summary>
public sealed class MediaSoapClient : IDisposable
{
    private readonly HttpClient _client;
    private readonly Func<CollectorOptions> _options;

    /// <summary>Creates a production transport with no proxy or redirect handling.</summary>
    /// <param name="options">Current collector configuration.</param>
    public MediaSoapClient(Func<CollectorOptions> options) : this(options, new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(3),
        MaxConnectionsPerServer = 2
    })
    {
    }

    /// <summary>Creates a transport with an injected handler for isolated protocol tests.</summary>
    /// <param name="options">Current collector configuration.</param>
    /// <param name="handler">Explicit test message handler.</param>
    public MediaSoapClient(Func<CollectorOptions> options, HttpMessageHandler handler)
    {
        this._options = options;
        this._client = new HttpClient(handler, true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>Reads only the fixed player description path.</summary>
    /// <param name="address">Literal approved address.</param>
    /// <param name="cancellationToken">Read cancellation.</param>
    /// <returns>Bounded description body.</returns>
    public Task<string> DescriptionAsync(string address, CancellationToken cancellationToken) =>
        this.SendAsync(address, "/xml/device_description.xml", null, null, cancellationToken);

    /// <summary>Reads a fixed local UPnP service contract without following advertised URLs.</summary>
    /// <param name="address">Literal approved address.</param>
    /// <param name="service">Whitelisted service name.</param>
    /// <param name="cancellationToken">Read cancellation.</param>
    /// <returns>Bounded SCPD XML.</returns>
    public Task<string> ServiceDescriptionAsync(string address, string service, CancellationToken cancellationToken)
    {
        if (service is not ("AVTransport" or "RenderingControl" or "GroupRenderingControl" or "ContentDirectory" or
            "DeviceProperties" or "ZoneGroupTopology"))
        {
            throw new ArgumentException("Unsupported service description.", nameof(service));
        }
        return this.SendAsync(address, $"/xml/{service}1.xml", null, null, cancellationToken);
    }

    /// <summary>Invokes one fixed service action with no retry or hedging.</summary>
    /// <param name="address">Literal approved address.</param>
    /// <param name="service">Whitelisted service.</param>
    /// <param name="action">Whitelisted action.</param>
    /// <param name="arguments">Protocol values.</param>
    /// <param name="cancellationToken">Call cancellation.</param>
    /// <returns>Bounded validated SOAP fields.</returns>
    public async Task<IReadOnlyDictionary<string, string>> CallAsync(string address, string service, string action,
        IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken)
    {
        string path = service switch
        {
            "AVTransport" or "RenderingControl" or "GroupRenderingControl" => $"/MediaRenderer/{service}/Control",
            "ContentDirectory" => "/MediaServer/ContentDirectory/Control",
            "DeviceProperties" => "/DeviceProperties/Control",
            "ZoneGroupTopology" => "/ZoneGroupTopology/Control",
            _ => throw new ArgumentException("Unsupported SOAP service.", nameof(service))
        };
        string request = MediaSoapProtocol.Build(service, action, arguments);
        string response = await this.SendAsync(address, path, request,
            $"urn:schemas-upnp-org:service:{service}:1#{action}", cancellationToken);
        return MediaSoapProtocol.Parse(response, service, action);
    }

    /// <summary>Applies current CIDR policy and bounded response reads to one request.</summary>
    /// <param name="address">Literal address.</param>
    /// <param name="path">Application-owned path.</param>
    /// <param name="body">SOAP body, or null for description GET.</param>
    /// <param name="soapAction">SOAP action header.</param>
    /// <param name="cancellationToken">Call cancellation.</param>
    /// <returns>Bounded UTF-8 response.</returns>
    private async Task<string> SendAsync(string address, string path, string? body, string? soapAction,
        CancellationToken cancellationToken)
    {
        CollectorOptions options = this._options();
        if (options.Enabled == false || new Ipv4TargetPolicy(options.AllowedCidrs, options.AllowLoopback).IsAllowed(address) == false)
        {
            throw new InvalidOperationException("Media target is outside the active collector policy.");
        }

        using HttpRequestMessage request = new(body is null ? HttpMethod.Get : HttpMethod.Post,
            new Uri($"http://{address}:1400{path}"));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "text/xml");
            request.Headers.Add("SOAPACTION", "\"" + soapAction + "\"");
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.RequestTimeout);
        using HttpResponseMessage response = await this._client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            throw new HttpRequestException("Device redirect is prohibited.");
        }

        if (response.Content is null)
        {
            throw new HttpRequestException("Device returned no response body.");
        }

        int limit = Math.Min(options.MaxResponseBytes, 256 * 1024);
        if (response.Content.Headers.ContentLength > limit)
        {
            throw new InvalidDataException("Device response exceeds the size limit.");
        }

        await using Stream input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[8192];
        while (true)
        {
            int read = await input.ReadAsync(chunk, timeout.Token);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > limit)
            {
                throw new InvalidDataException("Device response exceeds the size limit.");
            }
            buffer.Write(chunk, 0, read);
        }

        string text = Encoding.UTF8.GetString(buffer.ToArray());
        if (response.IsSuccessStatusCode == false)
        {
            // A device may carry an actionable SOAP fault in an HTTP 500 response.
            if (body is not null)
            {
                try
                {
                    MediaSoapProtocol.Parse(text, soapAction!.Split('#')[0].Split(':')[^2], soapAction.Split('#')[1]);
                }
                catch (MediaSoapFaultException)
                {
                    throw;
                }
                catch (FormatException)
                {
                    // Non-SOAP HTTP failures stay transport failures.
                }
            }
            throw new HttpRequestException($"Device returned HTTP {(int)response.StatusCode}.");
        }
        return text;
    }

    /// <summary>Closes owned HTTP connections.</summary>
    public void Dispose() => this._client.Dispose();
}
