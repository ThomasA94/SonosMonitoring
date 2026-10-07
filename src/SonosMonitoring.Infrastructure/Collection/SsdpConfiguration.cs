using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Validates explicit SSDP source interfaces and bounded collection settings at startup.</summary>
public static class SsdpConfiguration
{
    /// <summary>Rejects wildcard, unavailable, duplicate, or unapproved loopback source interfaces.</summary>
    /// <param name="options">Collector settings bound from deployment configuration.</param>
    public static void Validate(CollectorOptions options)
    {
        if (options.SsdpSearchWindow < TimeSpan.FromSeconds(2) || options.SsdpSearchWindow > TimeSpan.FromSeconds(10) ||
            options.SsdpMaxDatagrams < 1 || options.SsdpMaxDatagrams > 256 || options.SsdpInterfaceAddresses.Count > 8 ||
            options.SsdpInterfaceAddresses.Distinct(StringComparer.Ordinal).Count() != options.SsdpInterfaceAddresses.Count)
        {
            throw new ArgumentException("SSDP requires a 2-10 second window, 1-256 datagrams and at most eight distinct interfaces.", nameof(options));
        }

        if (options.SsdpEnabled == false)
        {
            return;
        }

        if (options.SsdpInterfaceAddresses.Count == 0 || options.SsdpSearchWindow >= options.SweepTimeout)
        {
            throw new ArgumentException("Enabled SSDP requires explicit local interfaces and a search window shorter than SweepTimeout.", nameof(options));
        }

        HashSet<string> localAddresses = NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address.ToString()).ToHashSet(StringComparer.Ordinal);
        foreach (string address in options.SsdpInterfaceAddresses)
        {
            if (IPAddress.TryParse(address, out IPAddress? parsed) == false || parsed.AddressFamily != AddressFamily.InterNetwork ||
                parsed.ToString() != address || localAddresses.Contains(address) == false ||
                (IPAddress.IsLoopback(parsed) && options.AllowLoopback == false))
            {
                throw new ArgumentException("SSDP interfaces must be assigned literal local IPv4 addresses; loopback requires Testing configuration.", nameof(options));
            }
        }
    }
}
