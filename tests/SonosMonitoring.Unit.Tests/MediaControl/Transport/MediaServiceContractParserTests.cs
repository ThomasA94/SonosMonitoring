using SonosMonitoring.Infrastructure.MediaControl.Transport;

namespace SonosMonitoring.Unit.Tests.MediaControl.Transport;

/// <summary>Checks exact action evidence from bounded local service descriptions.</summary>
public sealed class MediaServiceContractParserTests
{
    /// <summary>Accepts exact action names without inferring unavailable writes from the service name.</summary>
    [Fact]
    public void ParsesAdvertisedActions()
    {
        IReadOnlySet<string> actions = MediaServiceContractParser.ParseActions(
            "<scpd><actionList><action><name>GetVolume</name></action><action><name>SetVolume</name></action></actionList></scpd>");
        Assert.Contains("SetVolume", actions);
        Assert.DoesNotContain("SetMute", actions);
    }

    /// <summary>Rejects duplicate actions and external XML declarations.</summary>
    [Fact]
    public void RejectsDuplicateOrDtdContracts()
    {
        Assert.Throws<FormatException>(() => MediaServiceContractParser.ParseActions(
            "<scpd><actionList><action><name>Play</name></action><action><name>Play</name></action></actionList></scpd>"));
        Assert.Throws<FormatException>(() => MediaServiceContractParser.ParseActions(
            "<!DOCTYPE scpd><scpd><actionList><action><name>Play</name></action></actionList></scpd>"));
    }
}
