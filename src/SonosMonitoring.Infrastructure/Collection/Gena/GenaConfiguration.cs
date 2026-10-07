using System.Net;
using System.Net.Sockets;

namespace SonosMonitoring.Infrastructure.Collection.Gena;

/// <summary>Validates explicit callback routing and bounded event-processing configuration.</summary>
public static class GenaConfiguration
{
    /// <summary>Rejects unsafe or ambiguous listener settings before background services start.</summary>
    /// <param name="collector">Collector configuration including the testing-only loopback opt-in.</param>
    public static void Validate(CollectorOptions collector)
    {
        var options = collector.Gena;
        if (options.Enabled == false)
        {
            return;
        }
        if (collector.Enabled == false || ValidAddress(options.ListenAddress, true, collector.AllowLoopback) == false ||
            ValidAddress(options.CallbackAddress, false, collector.AllowLoopback) == false ||
            options.Port < 0 || options.Port > 65535 || (options.Port == 0 && collector.AllowLoopback == false) ||
            options.RequestedLeaseSeconds < 30 || options.RequestedLeaseSeconds > 86400 ||
            options.InitialNotificationTimeout < TimeSpan.FromSeconds(1) || options.InitialNotificationTimeout > TimeSpan.FromMinutes(5) ||
            options.MinimumPollInterval < TimeSpan.FromSeconds(1) || options.MinimumPollInterval > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentException("GENA requires enabled collection, explicit IPv4 callback routing and valid time limits.");
        }
    }

    /// <summary>Allows canonical unicast IPv4 literals; only a listener may use the wildcard address.</summary>
    /// <param name="value">Configured literal.</param>
    /// <param name="wildcard">Whether the any-address binding is meaningful here.</param>
    /// <param name="loopback">Whether isolated loopback tests were explicitly enabled.</param>
    private static bool ValidAddress(string? value, bool wildcard, bool loopback)
    {
        if (IPAddress.TryParse(value, out var address) == false || address.AddressFamily != AddressFamily.InterNetwork || address.ToString() != value)
        {
            return false;
        }
        if (address.Equals(IPAddress.Any))
        {
            return wildcard;
        }
        byte first = address.GetAddressBytes()[0];
        return first is > 0 and < 224 && (IPAddress.IsLoopback(address) == false || loopback);
    }
}
