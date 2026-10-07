using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Verifies that a foreign web server cannot be mistaken for a Sonos player.</summary>
public sealed class PlayerDescriptionParserTests
{
    private const string VALID = """
        <root xmlns="urn:schemas-upnp-org:device-1-0"><device>
          <deviceType>urn:schemas-upnp-org:device:ZonePlayer:1</deviceType>
          <manufacturer>Sonos, Inc.</manufacturer><modelName>Test Player</modelName>
          <roomName>Office</roomName><UDN>uuid:RINCON_123456789ABC01400</UDN>
          <serviceList><service><serviceType>urn:schemas-upnp-org:service:ZoneGroupTopology:1</serviceType></service></serviceList>
        </device></root>
        """;

    /// <summary>Uses normalized UDN identity and reads optional display fields.</summary>
    [Fact]
    public void ParsesValidatedPlayer()
    {
        PlayerDescription player = PlayerDescriptionParser.Parse(VALID);

        Assert.Equal("RINCON_123456789ABC01400", player.DeviceId);
        Assert.Equal("Office", player.RoomName);
        Assert.False(player.SupportsTopologyEvents);
    }

    /// <summary>Only the explicitly supported relative event path can enable subscriptions.</summary>
    /// <param name="path">Advertised event path or URL.</param>
    /// <param name="supported">Expected eligibility without changing device identity.</param>
    [Theory]
    [InlineData("/ZoneGroupTopology/Event", true)]
    [InlineData("ZoneGroupTopology/Event", true)]
    [InlineData("http://192.0.2.2:1400/ZoneGroupTopology/Event", false)]
    [InlineData("//192.0.2.2/ZoneGroupTopology/Event", false)]
    [InlineData("/other", false)]
    public void RequiresSupportedEventPath(string path, bool supported)
    {
        var xml = VALID.Replace("</serviceType>", $"</serviceType><eventSubURL>{path}</eventSubURL>", StringComparison.Ordinal);
        Assert.Equal(supported, PlayerDescriptionParser.Parse(xml).SupportsTopologyEvents);
    }

    /// <summary>Requires manufacturer, type, UDN, and topology service together.</summary>
    [Fact]
    public void RejectsForeignOrIncompleteDescription()
    {
        Assert.Throws<FormatException>(() => PlayerDescriptionParser.Parse(VALID.Replace("Sonos, Inc.", "Other Inc.")));
        Assert.Throws<FormatException>(() => PlayerDescriptionParser.Parse(VALID.Replace("ZoneGroupTopology", "ContentDirectory")));
        Assert.Throws<FormatException>(() => PlayerDescriptionParser.Parse("<html>ok</html>"));
    }

    /// <summary>Prohibits DTD entity expansion.</summary>
    [Fact]
    public void RejectsDtd()
    {
        string xml = "<!DOCTYPE root [<!ENTITY x 'invalid'>]>" + VALID;

        Assert.Throws<FormatException>(() => PlayerDescriptionParser.Parse(xml));
    }
}
