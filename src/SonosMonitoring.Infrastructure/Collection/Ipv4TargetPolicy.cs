using System.Net;
using System.Net.Sockets;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Restricts outbound requests to literal IPv4 addresses in explicit CIDRs.</summary>
public sealed class Ipv4TargetPolicy
{
    private readonly IReadOnlyList<Ipv4Range> _ranges;
    private readonly bool _allowLoopback;

    /// <summary>Parses and validates the configured address ranges.</summary>
    /// <param name="cidrs">Explicit IPv4 CIDRs.</param>
    /// <param name="allowLoopback">Allows loopback only for isolated tests.</param>
    public Ipv4TargetPolicy(IEnumerable<string> cidrs, bool allowLoopback = false)
    {
        this._ranges = cidrs.Select(Ipv4Range.Parse).ToArray();
        this._allowLoopback = allowLoopback;
        Ipv4Range loopback = Ipv4Range.Parse("127.0.0.0/8");
        if (allowLoopback == false && this._ranges.Any(range => range.Overlaps(loopback)))
        {
            throw new ArgumentException("Loopback targets are prohibited in production configuration.", nameof(cidrs));
        }
    }

    /// <summary>Gets the validated ranges in configured order.</summary>
    public IReadOnlyList<Ipv4Range> Ranges => this._ranges;

    /// <summary>Checks a literal address against the configured boundaries.</summary>
    /// <param name="address">An IPv4 literal, never a host name.</param>
    /// <returns>Whether the target is allowed.</returns>
    public bool IsAllowed(string address)
    {
        if (IPAddress.TryParse(address, out IPAddress? parsed) == false ||
            parsed.AddressFamily != AddressFamily.InterNetwork || parsed.ToString() != address)
        {
            return false;
        }

        if (this._allowLoopback == false && parsed.GetAddressBytes()[0] == 127)
        {
            return false;
        }

        return this._ranges.Any(range => range.Contains(parsed));
    }
}
