using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Verifies that adding a SOAP read cannot bypass the outbound target and action boundary.</summary>
public sealed class PlayerProbeTests
{
    /// <summary>Rejects unapproved addresses, hostnames and arbitrary action paths before network I/O.</summary>
    [Fact]
    public async Task RejectsTargetsAndPathsOutsideTheFixedReadContract()
    {
        using PlayerProbe probe = new(new Ipv4TargetPolicy(["192.168.6.0/24"], false), new CollectorOptions());
        await Assert.ThrowsAsync<InvalidOperationException>(() => probe.GetTopologyAsync("192.168.7.2", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => probe.GetTopologyAsync("example.com", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => probe.GetTopologyAsync("127.0.0.1", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => probe.GetAsync("192.168.6.2", "/MediaRenderer/AVTransport/Control", CancellationToken.None));
    }
}
