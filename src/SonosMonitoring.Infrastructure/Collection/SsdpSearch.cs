using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Sends a single bounded active SSDP search from one explicitly selected interface.</summary>
public sealed class SsdpSearch
{
    private readonly IPEndPoint _destination;

    /// <summary>Creates the production transport with the fixed SSDP multicast destination.</summary>
    public SsdpSearch() : this(new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900))
    {
    }

    /// <summary>Creates a loopback-test transport without a configurable production destination.</summary>
    /// <param name="destination">Isolated simulator endpoint.</param>
    internal SsdpSearch(IPEndPoint destination)
    {
        this._destination = destination;
    }

    /// <summary>Collects unicast replies until the receive window or datagram budget expires.</summary>
    /// <param name="localAddress">Explicit local interface IPv4 address.</param>
    /// <param name="window">Maximum search duration, including the send.</param>
    /// <param name="maxDatagrams">Maximum datagrams received, counting invalid and oversized replies.</param>
    /// <param name="cancellationToken">Sweep cancellation or host shutdown.</param>
    /// <returns>Bounded responses with socket failure and truncation status.</returns>
    public async Task<SsdpSearchResult> SearchAsync(string localAddress, TimeSpan window, int maxDatagrams, CancellationToken cancellationToken)
    {
        List<SsdpDatagram> datagrams = [];
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(window);
        try
        {
            using UdpClient client = new(new IPEndPoint(IPAddress.Parse(localAddress), 0));
            client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, IPAddress.Parse(localAddress).GetAddressBytes());
            client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
            byte[] request = Encoding.ASCII.GetBytes(
                "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 1\r\nST: " +
                SsdpResponseParser.SEARCH_TARGET + "\r\n\r\n");
            await client.SendAsync(request, this._destination, deadline.Token);
            for (int count = 0; count < maxDatagrams; count++)
            {
                UdpReceiveResult received = await client.ReceiveAsync(deadline.Token);
                if (received.Buffer.Length <= 8192)
                {
                    datagrams.Add(new SsdpDatagram(received.Buffer, received.RemoteEndPoint.Address.ToString()));
                }
            }

            return new SsdpSearchResult(datagrams, true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == false)
        {
            return new SsdpSearchResult(datagrams, false, null);
        }
        catch (SocketException)
        {
            return new SsdpSearchResult(datagrams, false, "socket_failure");
        }
    }
}
