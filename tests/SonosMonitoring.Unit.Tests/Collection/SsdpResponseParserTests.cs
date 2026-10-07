using System.Text;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Checks strict parsing and allowlist enforcement for SSDP replies.</summary>
public sealed class SsdpResponseParserTests
{
    private const string SENDER = "192.168.6.20";
    private const string DEVICE_ID = "RINCON_123456789ABC01400";
    private static readonly Ipv4TargetPolicy _policy = new(["192.168.6.0/24"]);

    /// <summary>Accepts a canonical Sonos response and returns its approved sender.</summary>
    [Fact]
    public void ParsesCanonicalSonosResponse()
    {
        string response = this.BuildResponse();

        Assert.Equal(SENDER, SsdpResponseParser.Parse(Encoding.ASCII.GetBytes(response), SENDER, _policy));
    }

    /// <summary>Accepts case-insensitive header names and surrounding header value whitespace.</summary>
    [Fact]
    public void AcceptsHeaderNameCasingAndValueWhitespace()
    {
        string response = this.BuildResponse()
            .Replace("ST:", "sT:", StringComparison.Ordinal)
            .Replace("USN:", "uSn:", StringComparison.Ordinal)
            .Replace("LOCATION:", "location:", StringComparison.Ordinal)
            .Replace("uSn: ", "uSn:\t ", StringComparison.Ordinal)
            .Replace("location: ", "location:   ", StringComparison.Ordinal);

        Assert.Equal(SENDER, SsdpResponseParser.Parse(Encoding.ASCII.GetBytes(response), SENDER, _policy));
    }

    /// <summary>Rejects an unsupported status line or a noncanonical status spelling.</summary>
    /// <param name="statusLine">The response status line to substitute.</param>
    [Theory]
    [InlineData("HTTP/1.1 500 Internal Server Error")]
    [InlineData("HTTP/1.0 200 OK")]
    [InlineData("http/1.1 200 OK")]
    [InlineData("HTTP/1.1 200 ok")]
    public void RejectsNoncanonicalStatusLine(string statusLine)
    {
        string response = this.BuildResponse().Replace("HTTP/1.1 200 OK", statusLine, StringComparison.Ordinal);

        Assert.Null(SsdpResponseParser.Parse(Encoding.ASCII.GetBytes(response), SENDER, _policy));
    }

    /// <summary>Rejects malformed framing, folded or invalid headers, and a response body.</summary>
    /// <param name="suffix">The malformed ending or body to append.</param>
    [Theory]
    [InlineData("\r\nbody\r\n\r\n")]
    [InlineData("\n")]
    [InlineData("extra-blank")]
    [InlineData("folded")]
    [InlineData("missing-colon")]
    public void RejectsMalformedFraming(string suffix)
    {
        string response = this.BuildResponse();
        if (suffix == "\n")
        {
            response = response.Replace("\r\n", "\n", StringComparison.Ordinal);
        }
        else if (suffix == "extra-blank")
        {
            response = response.Replace("\r\n\r\n", "\r\n\r\n\r\n", StringComparison.Ordinal);
        }
        else if (suffix == "folded")
        {
            response = response.Replace("\r\nUSN:", "\r\n continuation\r\nUSN:", StringComparison.Ordinal);
        }
        else if (suffix == "missing-colon")
        {
            response = response.Replace("ST:", "ST", StringComparison.Ordinal);
        }
        else
        {
            response = response.Replace("\r\n\r\n", suffix, StringComparison.Ordinal);
        }

        Assert.Null(SsdpResponseParser.Parse(Encoding.ASCII.GetBytes(response), SENDER, _policy));
    }

    /// <summary>Rejects malformed extension headers even when required fields remain valid.</summary>
    /// <param name="header">Malformed extension header.</param>
    [Theory]
    [InlineData("X: a\nInjected: b")]
    [InlineData("X: a\rInjected: b")]
    [InlineData("BAD NAME: x")]
    [InlineData("X@: x")]
    public void RejectsMalformedExtensionHeaders(string header)
    {
        string response = this.BuildResponse().Replace("\r\n\r\n", $"\r\n{header}\r\n\r\n", StringComparison.Ordinal);
        Assert.Null(SsdpResponseParser.Parse(Encoding.ASCII.GetBytes(response), SENDER, _policy));
    }

    /// <summary>Rejects duplicate required headers regardless of header-name casing.</summary>
    /// <param name="header">The duplicated header line.</param>
    [Theory]
    [InlineData("st: urn:schemas-upnp-org:device:ZonePlayer:1")]
    [InlineData("USN: uuid:RINCON_123456789ABC01400::urn:schemas-upnp-org:device:ZonePlayer:1")]
    [InlineData("LOCATION: http://192.168.6.20:1400/xml/device_description.xml")]
    public void RejectsDuplicateRequiredHeaders(string header)
    {
        string response = this.BuildResponse().Replace("\r\n\r\n", $"\r\n{header}\r\n\r\n", StringComparison.Ordinal);

        Assert.Null(SsdpResponseParser.Parse(Encoding.ASCII.GetBytes(response), SENDER, _policy));
    }

    /// <summary>Rejects a search target that differs from the requested Sonos device type.</summary>
    [Fact]
    public void RejectsWrongSearchTarget()
    {
        string response = this.BuildResponse().Replace(
            $"ST: {SsdpResponseParser.SEARCH_TARGET}",
            "ST: ssdp:all",
            StringComparison.Ordinal);

        Assert.Null(SsdpResponseParser.Parse(Encoding.ASCII.GetBytes(response), SENDER, _policy));
    }

    /// <summary>Rejects USNs that do not use a valid Sonos identity and the requested target.</summary>
    /// <param name="usn">The untrusted USN value.</param>
    [Theory]
    [InlineData("uuid:OTHER_123456789ABC01400::urn:schemas-upnp-org:device:ZonePlayer:1")]
    [InlineData("uuid:RINCON_bad-id::urn:schemas-upnp-org:device:ZonePlayer:1")]
    [InlineData("uuid:RINCON_123456789ABC01400::urn:schemas-upnp-org:device:ZonePlayer:2")]
    [InlineData("uuid:RINCON_123456789ABC01400")]
    [InlineData("uuid:RINCON_123456789ABC01400::urn:schemas-upnp-org:device:ZonePlayer:1::extra")]
    public void RejectsInvalidUsn(string usn)
    {
        string response = this.BuildResponse().Replace($"uuid:{DEVICE_ID}::{SsdpResponseParser.SEARCH_TARGET}", usn, StringComparison.Ordinal);

        Assert.Null(SsdpResponseParser.Parse(Encoding.ASCII.GetBytes(response), SENDER, _policy));
    }

    /// <summary>Rejects noncanonical or unsafe LOCATION URLs.</summary>
    /// <param name="location">The LOCATION value to substitute.</param>
    [Theory]
    [InlineData("http://user:pass@192.168.6.20:1400/xml/device_description.xml")]
    [InlineData("http://sonos.local:1400/xml/device_description.xml")]
    [InlineData("http://192.168.6.21:1400/xml/device_description.xml")]
    [InlineData("http://192.168.6.20:1401/xml/device_description.xml")]
    [InlineData("http://192.168.6.20:1400/other.xml")]
    [InlineData("http://192.168.6.20:1400/xml/device_description.xml?x=1")]
    [InlineData("http://192.168.6.20:1400/xml/device_description.xml#fragment")]
    [InlineData("http://[::1]:1400/xml/device_description.xml")]
    public void RejectsNoncanonicalLocation(string location)
    {
        string response = this.BuildResponse().Replace(
            $"http://{SENDER}:1400/xml/device_description.xml",
            location,
            StringComparison.Ordinal);

        Assert.Null(SsdpResponseParser.Parse(Encoding.ASCII.GetBytes(response), SENDER, _policy));
    }

    /// <summary>Rejects a sender outside the configured IPv4 CIDR.</summary>
    [Fact]
    public void RejectsOutOfPolicySender()
    {
        Assert.Null(SsdpResponseParser.Parse(Encoding.ASCII.GetBytes(this.BuildResponse()), "192.168.7.20", _policy));
    }

    /// <summary>Rejects payloads above the size limit and payload bytes outside ASCII.</summary>
    [Fact]
    public void RejectsOversizedAndNonAsciiPayloads()
    {
        byte[] oversized = new byte[8193];
        byte[] invalidAscii = Encoding.ASCII.GetBytes(this.BuildResponse());
        invalidAscii[0] = 0xFF;

        Assert.Null(SsdpResponseParser.Parse(oversized, SENDER, _policy));
        Assert.Null(SsdpResponseParser.Parse(invalidAscii, SENDER, _policy));
    }

    /// <summary>Builds a valid response using the parser's canonical Sonos target.</summary>
    /// <returns>A complete CRLF-framed SSDP response.</returns>
    private string BuildResponse()
    {
        return $"HTTP/1.1 200 OK\r\n" +
            $"ST: {SsdpResponseParser.SEARCH_TARGET}\r\n" +
            $"USN: uuid:{DEVICE_ID}::{SsdpResponseParser.SEARCH_TARGET}\r\n" +
            $"LOCATION: http://{SENDER}:1400/xml/device_description.xml\r\n\r\n";
    }
}
