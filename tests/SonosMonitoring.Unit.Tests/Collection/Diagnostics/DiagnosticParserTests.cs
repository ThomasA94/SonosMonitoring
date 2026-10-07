using SonosMonitoring.Infrastructure.Collection.Diagnostics;

namespace SonosMonitoring.Unit.Tests.Collection.Diagnostics;

/// <summary>Verifies read-only diagnostic XML parsers preserve supported values and reject partial data.</summary>
public sealed class DiagnosticParserTests
{
    private const string SOAP_NAMESPACE = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string AVTRANSPORT_NAMESPACE = "urn:schemas-upnp-org:service:AVTransport:1";
    private const string RENDERING_NAMESPACE = "urn:schemas-upnp-org:service:RenderingControl:1";

    /// <summary>Parses the observed wireless document and preserves a missing optional flag as null.</summary>
    [Fact]
    public void ParsesWirelessFieldsAndPreservesAbsentFlag()
    {
        string xml = """
            <ZPSupportInfo><Wireless><WirelessInfo Name="Wireless Info">
              <WifiModeString>STATION_MODE</WifiModeString><ConnectionTypeString>WiFi</ConnectionTypeString>
            </WirelessInfo></Wireless></ZPSupportInfo>
            """;

        SonosMonitoring.Application.Collection.Diagnostics.WirelessObservation result = WirelessStatusParser.Parse(xml);

        Assert.Equal("STATION_MODE", result.WifiMode);
        Assert.Equal("WiFi", result.ConnectionType);
        Assert.Null(result.SonosNetDisabled);
    }

    /// <summary>Reads the SonosNet flag as a boolean without confusing zero with an absent value.</summary>
    [Theory]
    [InlineData("0", false)]
    [InlineData("1", true)]
    public void ParsesWirelessFlag(string value, bool expected)
    {
        string xml = $"<ZPSupportInfo><Wireless><WirelessInfo><ConnectionTypeString>WiFi</ConnectionTypeString><SonosNetDisabled>{value}</SonosNetDisabled></WirelessInfo></Wireless></ZPSupportInfo>";

        Assert.Equal(expected, WirelessStatusParser.Parse(xml).SonosNetDisabled);
    }

    /// <summary>Requires at least one supported wireless enum and rejects malformed present values.</summary>
    [Fact]
    public void RejectsWirelessPartialOrMalformedFields()
    {
        Assert.Throws<FormatException>(() => WirelessStatusParser.Parse("<ZPSupportInfo><Wireless><WirelessInfo><SonosNetDisabled>0</SonosNetDisabled></WirelessInfo></Wireless></ZPSupportInfo>"));
        Assert.Throws<FormatException>(() => WirelessStatusParser.Parse("<ZPSupportInfo><Wireless><WirelessInfo><WifiModeString> </WifiModeString></WirelessInfo></Wireless></ZPSupportInfo>"));
        Assert.Throws<FormatException>(() => WirelessStatusParser.Parse("<ZPSupportInfo><Wireless><WirelessInfo><WifiModeString><Unexpected>STATION_MODE</Unexpected></WifiModeString></WirelessInfo></Wireless></ZPSupportInfo>"));
        Assert.Throws<FormatException>(() => WirelessStatusParser.Parse("<ZPSupportInfo><Wireless><WirelessInfo><ConnectionTypeString>WiFi</ConnectionTypeString><SonosNetDisabled><Unexpected>0</Unexpected></SonosNetDisabled></WirelessInfo></Wireless></ZPSupportInfo>"));
        Assert.Throws<FormatException>(() => WirelessStatusParser.Parse("<ZPSupportInfo><Wireless><WirelessInfo><ConnectionTypeString>WiFi</ConnectionTypeString><SonosNetDisabled>2</SonosNetDisabled></WirelessInfo></Wireless></ZPSupportInfo>"));
        Assert.Throws<FormatException>(() => WirelessStatusParser.Parse("<!DOCTYPE x [<!ENTITY e 'WiFi'>]><ZPSupportInfo><Wireless><WirelessInfo><ConnectionTypeString>&e;</ConnectionTypeString></WirelessInfo></Wireless></ZPSupportInfo>"));
    }

    /// <summary>Parses the observed port element and leaves the raw speed unit unspecified.</summary>
    [Fact]
    public void ParsesEthernetPortWithoutAssigningSpeedUnit()
    {
        IReadOnlyList<SonosMonitoring.Application.Collection.Diagnostics.EthernetPortObservation> ports = EthernetPortsParser.Parse(
            "<ZPSupportInfo><EnetPorts><Port port=\"0\"><Link>0</Link><Speed>0</Speed></Port></EnetPorts></ZPSupportInfo>");

        var port = Assert.Single(ports);
        Assert.Equal("0", port.Port);
        Assert.False(port.LinkUp);
        Assert.Equal(0, port.ReportedSpeed);
    }

    /// <summary>Rejects incomplete, invalid, and duplicate Ethernet port reports.</summary>
    [Fact]
    public void RejectsPartialEthernetPortEntries()
    {
        IReadOnlyList<SonosMonitoring.Application.Collection.Diagnostics.EthernetPortObservation> withoutSpeed = EthernetPortsParser.Parse(
            "<ZPSupportInfo><EnetPorts><Port port=\"0\"><Link>0</Link></Port></EnetPorts></ZPSupportInfo>");
        Assert.Null(Assert.Single(withoutSpeed).ReportedSpeed);
        Assert.Throws<FormatException>(() => EthernetPortsParser.Parse("<ZPSupportInfo><EnetPorts><Port port=\"0\"><Link>2</Link><Speed>0</Speed></Port></EnetPorts></ZPSupportInfo>"));
        Assert.Throws<FormatException>(() => EthernetPortsParser.Parse("<ZPSupportInfo><EnetPorts><Port port=\"0\"><Link>0</Link><Speed>-1</Speed></Port></EnetPorts></ZPSupportInfo>"));
        Assert.Throws<FormatException>(() => EthernetPortsParser.Parse("<ZPSupportInfo><EnetPorts><Port port=\"0\"><Link>0</Link><Speed>1x</Speed></Port></EnetPorts></ZPSupportInfo>"));
        Assert.Throws<FormatException>(() => EthernetPortsParser.Parse("<ZPSupportInfo><EnetPorts><Port port=\"0\"><Link><Unexpected>0</Unexpected></Link><Speed>0</Speed></Port></EnetPorts></ZPSupportInfo>"));
        Assert.Throws<FormatException>(() => EthernetPortsParser.Parse("<ZPSupportInfo><EnetPorts><Port port=\"0\"><Link>0</Link><Speed><Unexpected>0</Unexpected></Speed></Port></EnetPorts></ZPSupportInfo>"));
    }

    /// <summary>Reads state and status from the observed AVTransport SOAP response.</summary>
    [Fact]
    public void ParsesTransportInfo()
    {
        string response = this.WrapSoap(AVTRANSPORT_NAMESPACE, "GetTransportInfoResponse",
            "<CurrentTransportState>PLAYING</CurrentTransportState><CurrentTransportStatus>OK</CurrentTransportStatus><CurrentSpeed>1</CurrentSpeed>");

        SonosMonitoring.Application.Collection.Diagnostics.PlaybackObservation result = TransportInfoParser.Parse(response);

        Assert.Equal("PLAYING", result.TransportState);
        Assert.Equal("OK", result.TransportStatus);
    }

    /// <summary>Preserves a missing optional transport status as null and rejects partial actions.</summary>
    [Fact]
    public void HandlesMissingTransportStatusAndRejectsPartialResponse()
    {
        string response = this.WrapSoap(AVTRANSPORT_NAMESPACE, "GetTransportInfoResponse",
            "<CurrentTransportState>STOPPED</CurrentTransportState><CurrentSpeed>1</CurrentSpeed>");

        Assert.Null(TransportInfoParser.Parse(response).TransportStatus);
        Assert.Throws<FormatException>(() => TransportInfoParser.Parse(this.WrapSoap(AVTRANSPORT_NAMESPACE,
            "GetTransportInfoResponse", "<CurrentTransportState>PLAYING</CurrentTransportState>")));
        Assert.Throws<FormatException>(() => TransportInfoParser.Parse(response.Replace("STOPPED", "PLAYING ", StringComparison.Ordinal)));
    }

    /// <summary>Parses volume bounds and mute flags from read-only RenderingControl SOAP responses.</summary>
    [Theory]
    [InlineData("0", 0)]
    [InlineData("100", 100)]
    public void ParsesVolume(string value, int expected)
    {
        string response = this.WrapSoap(RENDERING_NAMESPACE, "GetVolumeResponse", $"<CurrentVolume>{value}</CurrentVolume>");

        Assert.Equal(expected, RenderingControlParser.ParseVolume(response));
    }

    /// <summary>Rejects out-of-range volume values and accepts only exact mute flags.</summary>
    [Fact]
    public void RejectsInvalidRenderingControlValues()
    {
        Assert.Throws<FormatException>(() => RenderingControlParser.ParseVolume(this.WrapSoap(RENDERING_NAMESPACE,
            "GetVolumeResponse", "<CurrentVolume>101</CurrentVolume>")));
        Assert.False(RenderingControlParser.ParseMute(this.WrapSoap(RENDERING_NAMESPACE,
            "GetMuteResponse", "<CurrentMute>0</CurrentMute>")));
        Assert.True(RenderingControlParser.ParseMute(this.WrapSoap(RENDERING_NAMESPACE,
            "GetMuteResponse", "<CurrentMute>1</CurrentMute>")));
        Assert.Throws<FormatException>(() => RenderingControlParser.ParseMute(this.WrapSoap(RENDERING_NAMESPACE,
            "GetMuteResponse", "<CurrentMute>true</CurrentMute>")));
    }

    /// <summary>Wraps one result body in an UPnP SOAP response.</summary>
    /// <param name="serviceNamespace">The UPnP service namespace.</param>
    /// <param name="responseName">The action response name.</param>
    /// <param name="outputs">The response's scalar output elements.</param>
    /// <returns>A SOAP envelope containing the action response.</returns>
    private string WrapSoap(string serviceNamespace, string responseName, string outputs)
    {
        return $"<s:Envelope xmlns:s=\"{SOAP_NAMESPACE}\" xmlns:u=\"{serviceNamespace}\"><s:Body><u:{responseName}>{outputs}</u:{responseName}></s:Body></s:Envelope>";
    }
}
