using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Verifies extraction of raw counters without inventing missing values.</summary>
public sealed class IfconfigParserTests
{
    /// <summary>Reads packet and byte counters from conventional ifconfig output.</summary>
    [Fact]
    public void ParsesCompleteInterface()
    {
        string response = """
            <html><pre>br0      Link encap:Ethernet  HWaddr 00:00:00:00:00:00
                      RX packets:2540392 errors:2 dropped:392360 overruns:0 frame:0
                      TX packets:2645499 errors:0 dropped:7 overruns:0 carrier:0
                      RX bytes:978257218 (933.0 MiB)  TX bytes:1220226800 (1.1 GiB)
            </pre></html>
            """;

        var counter = Assert.Single(IfconfigParser.Parse(response));
        Assert.Equal("br0", counter.Interface);
        Assert.Equal(978257218UL, counter.ReceiveBytes);
        Assert.Equal(392360UL, counter.ReceiveDropped);
        Assert.Equal(7UL, counter.TransmitDropped);
    }

    /// <summary>Omits incomplete interface blocks.</summary>
    [Fact]
    public void OmitsMissingCounters()
    {
        Assert.Empty(IfconfigParser.Parse("br0 Link encap:Ethernet\n RX packets:1 errors:0 dropped:0"));
    }
}
