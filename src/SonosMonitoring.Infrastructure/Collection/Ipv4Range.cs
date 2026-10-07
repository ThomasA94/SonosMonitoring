using System.Net;
using System.Net.Sockets;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>A normalized IPv4 CIDR with bounded host enumeration.</summary>
public sealed class Ipv4Range
{
    private readonly uint _network;
    private readonly uint _mask;

    private Ipv4Range(uint network, int prefixLength)
    {
        this._mask = prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);
        this._network = network & this._mask;
        this.PrefixLength = prefixLength;
    }

    /// <summary>Gets the CIDR prefix length.</summary>
    public int PrefixLength { get; }

    /// <summary>Gets the canonical CIDR.</summary>
    public override string ToString()
    {
        return $"{new IPAddress(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)this._network)))}/{this.PrefixLength}";
    }

    /// <summary>Parses a literal IPv4 CIDR and rejects ambiguous input.</summary>
    /// <param name="value">Network address and prefix.</param>
    /// <returns>The normalized range.</returns>
    public static Ipv4Range Parse(string value)
    {
        string[] parts = value.Split('/', StringSplitOptions.None);
        if (parts.Length != 2 || IPAddress.TryParse(parts[0], out IPAddress? address) == false ||
            address.AddressFamily != AddressFamily.InterNetwork || address.ToString() != parts[0] ||
            int.TryParse(parts[1], out int prefix) == false || prefix < 0 || prefix > 32)
        {
            throw new ArgumentException("Expected a literal IPv4 CIDR.", nameof(value));
        }

        uint number = (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(address.GetAddressBytes()));
        return new Ipv4Range(number, prefix);
    }

    /// <summary>Checks whether an IPv4 address belongs to this range.</summary>
    /// <param name="address">Address to check.</param>
    /// <returns>Whether the address is inside this range.</returns>
    public bool Contains(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        uint number = (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(address.GetAddressBytes()));
        return (number & this._mask) == this._network;
    }

    /// <summary>Checks whether two CIDRs share any IPv4 address.</summary>
    /// <param name="other">Other range.</param>
    /// <returns>Whether the ranges overlap.</returns>
    public bool Overlaps(Ipv4Range other)
    {
        int prefix = Math.Min(this.PrefixLength, other.PrefixLength);
        uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
        return (this._network & mask) == (other._network & mask);
    }

    /// <summary>Enumerates usable host addresses, rejecting ranges beyond the configured budget.</summary>
    /// <param name="maximum">Maximum host count.</param>
    /// <returns>Literal IPv4 host addresses.</returns>
    public IEnumerable<string> EnumerateHosts(int maximum)
    {
        ulong count = 1UL << (32 - this.PrefixLength);
        ulong usable = this.PrefixLength <= 30 ? count - 2 : count;
        if (usable > (ulong)maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), "Range exceeds the scan budget.");
        }

        ulong start = this.PrefixLength <= 30 ? 1UL : 0UL;
        ulong end = this.PrefixLength <= 30 ? count - 1 : count;
        for (ulong offset = start; offset < end; offset++)
        {
            uint address = checked(this._network + (uint)offset);
            yield return new IPAddress(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)address))).ToString();
        }
    }
}
