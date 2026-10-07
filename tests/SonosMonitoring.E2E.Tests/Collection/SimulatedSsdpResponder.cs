using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.E2E.Tests.Collection;

/// <summary>Answers isolated unicast UDP searches without sending multicast or contacting real devices.</summary>
public sealed class SimulatedSsdpResponder : IAsyncDisposable
{
    private readonly UdpClient _socket = new(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0));
    private readonly CancellationTokenSource _shutdown = new();
    private readonly IReadOnlyList<byte[]> _responses;
    private readonly Task _loop;

    /// <summary>Starts a responder that returns the supplied datagrams for each request.</summary>
    /// <param name="responses">Synthetic response datagrams.</param>
    public SimulatedSsdpResponder(params string[] responses)
    {
        this._responses = responses.Select(Encoding.ASCII.GetBytes).ToArray();
        this._loop = this.RunAsync();
    }

    /// <summary>Gets the isolated transport destination.</summary>
    public IPEndPoint Endpoint => (IPEndPoint)this._socket.Client.LocalEndPoint!;

    /// <summary>Gets captured requests and their source addresses.</summary>
    public ConcurrentQueue<(string Request, string Source)> Requests { get; } = new();

    /// <summary>Creates an otherwise valid Sonos advertisement for URL boundary tests.</summary>
    /// <param name="locationAddress">Advertised description address.</param>
    /// <returns>A complete HTTPU response.</returns>
    public static string Reply(string locationAddress = "127.0.0.2") =>
        "HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=1800\r\nEXT:\r\nSERVER: Test UPnP/1.0 Sonos/1.0\r\n" +
        $"ST: {SsdpResponseParser.SEARCH_TARGET}\r\nUSN: uuid:RINCON_FIRST::{SsdpResponseParser.SEARCH_TARGET}\r\n" +
        $"LOCATION: http://{locationAddress}:1400/xml/device_description.xml\r\n\r\n";

    /// <summary>Cancels receiving and releases the local UDP socket.</summary>
    public async ValueTask DisposeAsync()
    {
        await this._shutdown.CancelAsync();
        try
        {
            await this._loop;
        }
        catch (OperationCanceledException)
        {
        }
        this._socket.Dispose();
        this._shutdown.Dispose();
    }

    /// <summary>Records each request and sends only the explicitly supplied synthetic replies.</summary>
    /// <returns>A task ending when the responder is disposed.</returns>
    private async Task RunAsync()
    {
        while (true)
        {
            UdpReceiveResult request = await this._socket.ReceiveAsync(this._shutdown.Token);
            this.Requests.Enqueue((Encoding.ASCII.GetString(request.Buffer), request.RemoteEndPoint.Address.ToString()));
            foreach (byte[] response in this._responses)
            {
                await this._socket.SendAsync(response, request.RemoteEndPoint, this._shutdown.Token);
            }
        }
    }
}
