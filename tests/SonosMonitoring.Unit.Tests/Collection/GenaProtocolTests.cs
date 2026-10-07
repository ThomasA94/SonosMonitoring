using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Checks GENA notification parsing, sequence rules, and subscription lease grants.</summary>
public sealed class GenaProtocolTests
{
    private const string SID = "uuid:subscription-123";
    private const string EVENT_NAMESPACE = "urn:schemas-upnp-org:event-1-0";

    /// <summary>Recognizes a valid topology property in a UPnP event property set.</summary>
    [Fact]
    public void ParsesZoneGroupStateProperty()
    {
        string xml = $"<e:propertyset xmlns:e=\"{EVENT_NAMESPACE}\"><e:property><ZoneGroupState>&lt;ZoneGroups /&gt;</ZoneGroupState></e:property></e:propertyset>";

        Assert.True(GenaNotificationParser.Parse(xml));
    }

    /// <summary>Accepts a valid event containing other properties without requesting a topology poll.</summary>
    [Fact]
    public void IgnoresOtherValidProperties()
    {
        string xml = $"<e:propertyset xmlns:e=\"{EVENT_NAMESPACE}\"><e:property><SomeState>value</SomeState></e:property></e:propertyset>";

        Assert.False(GenaNotificationParser.Parse(xml));
    }

    /// <summary>Rejects a property set with the wrong or absent namespace.</summary>
    /// <param name="xml">The event XML to reject.</param>
    [Theory]
    [InlineData("<propertyset><property><ZoneGroupState /></property></propertyset>")]
    [InlineData("<e:propertyset xmlns:e=\"urn:wrong\"><e:property><ZoneGroupState /></e:property></e:propertyset>")]
    public void RejectsWrongPropertySetNamespace(string xml)
    {
        Assert.Throws<FormatException>(() => GenaNotificationParser.Parse(xml));
    }

    /// <summary>Rejects malformed property structure, including multiple values and nested markup.</summary>
    /// <param name="xml">The event XML to reject.</param>
    [Theory]
    [InlineData("<e:propertyset xmlns:e=\"urn:schemas-upnp-org:event-1-0\"><e:property /></e:propertyset>")]
    [InlineData("<e:propertyset xmlns:e=\"urn:schemas-upnp-org:event-1-0\"><e:property><A /><B /></e:property></e:propertyset>")]
    [InlineData("<e:propertyset xmlns:e=\"urn:schemas-upnp-org:event-1-0\"><e:property><A><B /></A></e:property></e:propertyset>")]
    [InlineData("<e:propertyset xmlns:e=\"urn:schemas-upnp-org:event-1-0\"><other /></e:propertyset>")]
    public void RejectsMalformedPropertyStructure(string xml)
    {
        Assert.Throws<FormatException>(() => GenaNotificationParser.Parse(xml));
    }

    /// <summary>Rejects DTD declarations and payloads beyond the parser bound.</summary>
    [Fact]
    public void RejectsMaliciousOrOversizedXml()
    {
        string malicious = $"<!DOCTYPE x [<!ENTITY payload 'x'>]><e:propertyset xmlns:e=\"{EVENT_NAMESPACE}\"><e:property><ZoneGroupState>&payload;</ZoneGroupState></e:property></e:propertyset>";
        string oversized = new('x', 1024 * 1024 + 1);

        Assert.Throws<FormatException>(() => GenaNotificationParser.Parse(malicious));
        Assert.Throws<FormatException>(() => GenaNotificationParser.Parse(oversized));
    }

    /// <summary>Parses valid notification headers and preserves their validated values.</summary>
    [Fact]
    public void ParsesValidNotificationHeaders()
    {
        GenaHeaders headers = GenaHeaders.Parse(SID, "upnp:event", "upnp:propchange", "0");

        Assert.Equal(SID, headers.Sid);
        Assert.Equal(0u, headers.Sequence);
    }

    /// <summary>Rejects invalid SID, type, subtype, and sequence values.</summary>
    /// <param name="sid">The SID to parse.</param>
    /// <param name="nt">The NT header value.</param>
    /// <param name="nts">The NTS header value.</param>
    /// <param name="sequence">The SEQ header value.</param>
    [Theory]
    [InlineData("uuid:x", "wrong", "upnp:propchange", "1")]
    [InlineData("uuid:x", "upnp:event", "wrong", "1")]
    [InlineData("uuid:x", "upnp:event", "upnp:propchange", "")]
    [InlineData("uuid:x", "upnp:event", "upnp:propchange", "+1")]
    [InlineData("uuid:x", "upnp:event", "upnp:propchange", " 1")]
    [InlineData("uuid:x", "upnp:event", "upnp:propchange", "4294967296")]
    [InlineData("UUID:x", "upnp:event", "upnp:propchange", "1")]
    [InlineData("uuid:bad value", "upnp:event", "upnp:propchange", "1")]
    public void RejectsInvalidNotificationHeaders(string sid, string nt, string nts, string sequence)
    {
        Assert.Throws<FormatException>(() => GenaHeaders.Parse(sid, nt, nts, sequence));
    }

    /// <summary>Checks normal sequence increments and the specified uint32 wrap behavior.</summary>
    [Fact]
    public void ChecksSequenceSuccessorsAndRollover()
    {
        Assert.False(GenaHeaders.IsSuccessor(0, 1));
        Assert.True(GenaHeaders.IsSuccessor(1, 2));
        Assert.False(GenaHeaders.IsSuccessor(1, 1));
        Assert.False(GenaHeaders.IsSuccessor(1, 3));
        Assert.True(GenaHeaders.IsSuccessor(uint.MaxValue, 1));
        Assert.False(GenaHeaders.IsSuccessor(uint.MaxValue, 0));
    }

    /// <summary>Parses finite and infinite lease grants while retaining the SID.</summary>
    [Fact]
    public void ParsesFiniteAndInfiniteLeaseGrants()
    {
        GenaLeaseGrant finite = GenaLeaseGrant.Parse(SID, "Second-1800");
        GenaLeaseGrant infinite = GenaLeaseGrant.Parse(SID, "Second-infinite");

        Assert.Equal(SID, finite.Sid);
        Assert.Equal(TimeSpan.FromSeconds(1800), finite.Duration);
        Assert.Null(infinite.Duration);
    }

    /// <summary>Rejects malformed SID and timeout lease grants.</summary>
    /// <param name="sid">The SID response value.</param>
    /// <param name="timeout">The timeout response value.</param>
    [Theory]
    [InlineData("", "Second-30")]
    [InlineData("uuid:bad value", "Second-30")]
    [InlineData("uuid:x", "30")]
    [InlineData("uuid:x", "Second-0")]
    [InlineData("uuid:x", "Second--1")]
    [InlineData("uuid:x", "Second-2147483648")]
    [InlineData("uuid:x", "Second-1x")]
    public void RejectsInvalidLeaseGrants(string sid, string timeout)
    {
        Assert.Throws<FormatException>(() => GenaLeaseGrant.Parse(sid, timeout));
    }
}
