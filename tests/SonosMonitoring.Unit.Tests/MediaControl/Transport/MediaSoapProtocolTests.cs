using SonosMonitoring.Infrastructure.MediaControl.Transport;

namespace SonosMonitoring.Unit.Tests.MediaControl.Transport;

/// <summary>Checks the fixed SOAP boundary and bounded fault parsing.</summary>
public sealed class MediaSoapProtocolTests
{
    /// <summary>Escapes values and refuses a caller-selected service or action.</summary>
    [Fact]
    public void BuildsOnlyWhitelistedActionsWithEscapedArguments()
    {
        string body = MediaSoapProtocol.Build("AVTransport", "SetAVTransportURI",
            new Dictionary<string, string> { ["InstanceID"] = "0", ["CurrentURI"] = "http://a/?x=1&y=2" });
        Assert.Contains("x=1&amp;y=2", body, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => MediaSoapProtocol.Build("Unknown", "DeleteAll", new Dictionary<string, string>()));
    }

    /// <summary>Recognizes a SOAP fault in an otherwise valid HTTP response body.</summary>
    [Fact]
    public void ParsesSoapFaultWithoutLeakingDeviceText()
    {
        const string FAULT = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><detail><UPnPError><errorCode>701</errorCode><errorDescription>secret</errorDescription></UPnPError></detail></s:Fault></s:Body></s:Envelope>";
        MediaSoapFaultException exception = Assert.Throws<MediaSoapFaultException>(() =>
            MediaSoapProtocol.Parse(FAULT, "AVTransport", "Play"));
        Assert.Equal("701", exception.Code);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects external entities and mismatched action responses.</summary>
    [Fact]
    public void RejectsDtdAndWrongActionResponse()
    {
        Assert.Throws<FormatException>(() => MediaSoapProtocol.ReadDocument("<!DOCTYPE x [<!ENTITY z SYSTEM 'file:///etc/passwd'>]><x>&z;</x>"));
        const string WRONG = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:PauseResponse xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\" /></s:Body></s:Envelope>";
        Assert.Throws<FormatException>(() => MediaSoapProtocol.Parse(WRONG, "AVTransport", "Play"));
    }
}
