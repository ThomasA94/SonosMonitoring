using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Verifies that active discovery cannot silently choose broad or unintended interfaces.</summary>
public sealed class SsdpConfigurationTests
{
    /// <summary>Allows safe disabled defaults without enumerating or selecting a source interface.</summary>
    [Fact]
    public void AllowsDisabledDefaults()
    {
        SsdpConfiguration.Validate(new CollectorOptions());
    }

    /// <summary>Rejects missing source interfaces and budgets that consume the entire sweep.</summary>
    [Fact]
    public void RequiresExplicitInterfacesAndRemainingSweepBudget()
    {
        Assert.Throws<ArgumentException>(() => SsdpConfiguration.Validate(new CollectorOptions { SsdpEnabled = true }));
        Assert.Throws<ArgumentException>(() => SsdpConfiguration.Validate(new CollectorOptions
        {
            SsdpEnabled = true,
            SsdpInterfaceAddresses = ["127.0.0.1"],
            AllowLoopback = true,
            SsdpSearchWindow = TimeSpan.FromSeconds(3),
            SweepTimeout = TimeSpan.FromSeconds(3)
        }));
    }

    /// <summary>Rejects wildcard, DNS, IPv6, unassigned, and production loopback sources.</summary>
    /// <param name="source">Invalid source configuration.</param>
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("localhost")]
    [InlineData("::1")]
    [InlineData("192.0.2.99")]
    [InlineData("127.0.0.1")]
    public void RejectsUnapprovedSources(string source)
    {
        Assert.Throws<ArgumentException>(() => SsdpConfiguration.Validate(new CollectorOptions
        {
            SsdpEnabled = true,
            SsdpInterfaceAddresses = [source]
        }));
    }

    /// <summary>Rejects duplicate interfaces and out-of-range response budgets.</summary>
    [Fact]
    public void RejectsUnboundedOrDuplicateConfiguration()
    {
        Assert.Throws<ArgumentException>(() => SsdpConfiguration.Validate(new CollectorOptions { SsdpMaxDatagrams = 0 }));
        Assert.Throws<ArgumentException>(() => SsdpConfiguration.Validate(new CollectorOptions { SsdpMaxDatagrams = 257 }));
        Assert.Throws<ArgumentException>(() => SsdpConfiguration.Validate(new CollectorOptions { SsdpSearchWindow = TimeSpan.FromSeconds(1) }));
        Assert.Throws<ArgumentException>(() => SsdpConfiguration.Validate(new CollectorOptions { SsdpSearchWindow = TimeSpan.FromSeconds(11) }));
        Assert.Throws<ArgumentException>(() => SsdpConfiguration.Validate(new CollectorOptions { SsdpInterfaceAddresses = ["127.0.0.1", "127.0.0.1"] }));
    }
}
