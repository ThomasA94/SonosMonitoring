using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Verifies that discovery cannot escape explicit IPv4 boundaries.</summary>
public sealed class Ipv4TargetPolicyTests
{
    /// <summary>Accepts only canonical IPv4 literals inside configured CIDRs.</summary>
    [Fact]
    public void AllowsOnlyCanonicalTargetsInsideRanges()
    {
        Ipv4TargetPolicy policy = new(["192.168.6.0/24", "10.4.2.8/32"]);

        Assert.True(policy.IsAllowed("192.168.6.15"));
        Assert.True(policy.IsAllowed("10.4.2.8"));
        Assert.False(policy.IsAllowed("192.168.7.15"));
        Assert.False(policy.IsAllowed("localhost"));
        Assert.False(policy.IsAllowed("192.168.6.15.example"));
        Assert.False(policy.IsAllowed("127.0.0.1"));
    }

    /// <summary>Rejects broad or explicit ranges that would permit loopback.</summary>
    [Fact]
    public void RejectsLoopbackTargets()
    {
        Assert.Throws<ArgumentException>(() => new Ipv4TargetPolicy(["127.0.0.0/8"]));
        Assert.Throws<ArgumentException>(() => new Ipv4TargetPolicy(["127.0.0.2/32"]));
        Assert.Throws<ArgumentException>(() => new Ipv4TargetPolicy(["0.0.0.0/0"]));
        Assert.True(new Ipv4TargetPolicy(["127.0.0.2/32"], allowLoopback: true).IsAllowed("127.0.0.2"));
    }

    /// <summary>Limits enumeration before allocating or probing a large network.</summary>
    [Fact]
    public void RejectsOversizedEnumeration()
    {
        Ipv4Range range = Ipv4Range.Parse("192.168.0.0/16");

        Assert.Throws<ArgumentOutOfRangeException>(() => range.EnumerateHosts(1024).ToArray());
        Assert.Equal(2, Ipv4Range.Parse("10.1.2.0/30").EnumerateHosts(2).Count());
    }
}
