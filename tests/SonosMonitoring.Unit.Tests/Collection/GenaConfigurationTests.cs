using SonosMonitoring.Infrastructure.Collection;
using SonosMonitoring.Infrastructure.Collection.Gena;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Rejects ambiguous callback destinations before a listener or subscription starts.</summary>
public sealed class GenaConfigurationTests
{
    /// <summary>Callback advertisements must be canonical explicit unicast IPv4 addresses.</summary>
    /// <param name="address">Unsupported configured callback address.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("239.255.255.250")]
    [InlineData("::1")]
    [InlineData("192.0.2.1/path")]
    public void RejectsInvalidCallback(string? address)
    {
        var options = new CollectorOptions { Enabled = true, Gena = new() { Enabled = true, CallbackAddress = address } };
        Assert.Throws<ArgumentException>(() => GenaConfiguration.Validate(options));
    }

    /// <summary>Explicit unicast configuration permits the wildcard listener, but not ephemeral production ports.</summary>
    [Fact]
    public void AllowsExplicitCallbackWithBoundedProductionListener()
    {
        var options = new CollectorOptions { Enabled = true, Gena = new() { Enabled = true, CallbackAddress = "192.0.2.1" } };
        GenaConfiguration.Validate(options);
        options.Gena.Port = 0;
        Assert.Throws<ArgumentException>(() => GenaConfiguration.Validate(options));
        options.Gena.Enabled = false;
        GenaConfiguration.Validate(options);
    }
}
