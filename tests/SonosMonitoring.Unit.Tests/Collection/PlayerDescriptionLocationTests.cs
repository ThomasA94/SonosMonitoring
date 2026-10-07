using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Verifies strict extraction of addresses from advertised player-description locations.</summary>
public sealed class PlayerDescriptionLocationTests
{
    /// <summary>Returns the host only when the complete URL matches the canonical description location.</summary>
    [Fact]
    public void ExtractsAddressFromCanonicalLocation()
    {
        Assert.Equal("192.168.6.20", PlayerDescriptionLocation.GetAddress("http://192.168.6.20:1400/xml/device_description.xml"));
    }

    /// <summary>Rejects absent, noncanonical, unsafe, and oversized location values.</summary>
    /// <param name="location">Untrusted location text.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://sonos.local:1400/xml/device_description.xml")]
    [InlineData("http://user@192.168.6.20:1400/xml/device_description.xml")]
    [InlineData("http://192.168.6.20:1400/xml/device_description.xml?x=1")]
    [InlineData("http://192.168.6.20:1400/xml/device_description.xml#fragment")]
    [InlineData("http://192.168.6.20:1401/xml/device_description.xml")]
    [InlineData("http://192.168.6.20:1400/other.xml")]
    [InlineData("http://192.168.006.20:1400/xml/device_description.xml")]
    [InlineData("http://[::1]:1400/xml/device_description.xml")]
    public void RejectsUnsupportedLocations(string? location)
    {
        Assert.Null(PlayerDescriptionLocation.GetAddress(location));
    }

    /// <summary>Rejects a location longer than the parser's input bound.</summary>
    [Fact]
    public void RejectsOversizedLocation()
    {
        string location = "http://192.168.6.20:1400/xml/device_description.xml" + new string('a', 129);

        Assert.Null(PlayerDescriptionLocation.GetAddress(location));
    }
}
