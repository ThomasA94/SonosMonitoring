using System.Xml.Linq;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Verifies parsing and validation of read-only Sonos zone group topology.</summary>
public sealed class ZoneGroupTopologyParserTests
{
    private const string SOAP_NAMESPACE = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string SERVICE_NAMESPACE = "urn:schemas-upnp-org:service:ZoneGroupTopology:1";
    private const string COORDINATOR_ID = "RINCON_123456789ABC01400";
    private const string MEMBER_ID = "RINCON_123456789ABD01400";

    /// <summary>Parses groups, direct members, and nested satellites while retaining source values.</summary>
    [Fact]
    public void ParsesGroupWithNestedSatellite()
    {
        string topology = $"""
            <ZoneGroupState><ZoneGroups>
              <ZoneGroup ID="group:one" Coordinator="{COORDINATOR_ID}">
                <ZoneGroupMember UUID="{COORDINATOR_ID}" ZoneName="Living Room" Invisible="0" Location="http://example.invalid/device.xml">
                  <Satellite UUID="{MEMBER_ID}" ZoneName="Living Room" Invisible="1" Location="http://example.invalid/satellite.xml" />
                </ZoneGroupMember>
              </ZoneGroup>
            </ZoneGroups></ZoneGroupState>
            """;

        IReadOnlyList<SonosMonitoring.Application.Collection.TopologyGroup> groups = ZoneGroupTopologyParser.Parse(this.Wrap(topology), 2);

        SonosMonitoring.Application.Collection.TopologyGroup group = Assert.Single(groups);
        Assert.Equal("group:one", group.GroupId);
        Assert.Equal(COORDINATOR_ID, group.CoordinatorDeviceId);
        Assert.Collection(
            group.Members,
            coordinator =>
            {
                Assert.Equal(COORDINATOR_ID, coordinator.DeviceId);
                Assert.Equal("Living Room", coordinator.RoomName);
                Assert.False(coordinator.IsInvisible);
                Assert.Null(coordinator.ParentDeviceId);
            },
            satellite =>
            {
                Assert.Equal(MEMBER_ID, satellite.DeviceId);
                Assert.True(satellite.IsInvisible);
                Assert.Equal(COORDINATOR_ID, satellite.ParentDeviceId);
            });
    }

    /// <summary>Accepts current and legacy embedded topology root forms.</summary>
    /// <param name="topology">One supported embedded document shape.</param>
    [Theory]
    [InlineData("<ZoneGroups><ZoneGroup ID=\"g\" Coordinator=\"RINCON_123456789ABC01400\"><ZoneGroupMember UUID=\"RINCON_123456789ABC01400\" /></ZoneGroup></ZoneGroups>")]
    [InlineData("<ZoneGroupState><ZoneGroups><ZoneGroup ID=\"g\" Coordinator=\"RINCON_123456789ABC01400\"><ZoneGroupMember UUID=\"RINCON_123456789ABC01400\" /></ZoneGroup></ZoneGroups></ZoneGroupState>")]
    [InlineData("<ZoneGroupState><ZoneGroup ID=\"g\" Coordinator=\"RINCON_123456789ABC01400\"><ZoneGroupMember UUID=\"RINCON_123456789ABC01400\" /></ZoneGroup></ZoneGroupState>")]
    public void ParsesSupportedEmbeddedRootShapes(string topology)
    {
        IReadOnlyList<SonosMonitoring.Application.Collection.TopologyGroup> groups = ZoneGroupTopologyParser.Parse(this.Wrap(topology), 1);

        Assert.Equal("g", Assert.Single(groups).GroupId);
    }

    /// <summary>Normalizes a valid RINCON identifier to uppercase and accepts optional flag absence.</summary>
    [Fact]
    public void NormalizesMemberIdentifierAndPreservesMissingFlagAsNull()
    {
        const string LOWERCASE_ID = "rincon_123456789abc01400";
        string topology = $"<ZoneGroups><ZoneGroup ID=\"g\" Coordinator=\"{LOWERCASE_ID}\"><ZoneGroupMember UUID=\"{LOWERCASE_ID}\" /></ZoneGroup></ZoneGroups>";

        SonosMonitoring.Application.Collection.TopologyMember member = Assert.Single(Assert.Single(ZoneGroupTopologyParser.Parse(this.Wrap(topology), 1)).Members);

        Assert.Equal(COORDINATOR_ID, member.DeviceId);
        Assert.Null(member.IsInvisible);
    }

    /// <summary>Rejects envelopes or SOAP actions other than GetZoneGroupState.</summary>
    [Fact]
    public void RejectsWrongSoapEnvelopeOrAction()
    {
        string topology = this.ValidTopology();
        string wrongAction = this.Wrap(topology).Replace("GetZoneGroupStateResponse", "OtherResponse", StringComparison.Ordinal);
        string wrongEnvelope = this.Wrap(topology).Replace(SOAP_NAMESPACE, "urn:wrong-envelope", StringComparison.Ordinal);
        string wrongOutput = this.Wrap(topology).Replace("<ZoneGroupState>", "<ZoneGroupState xmlns=\"urn:foreign\">", StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => ZoneGroupTopologyParser.Parse(wrongAction, 4));
        Assert.Throws<FormatException>(() => ZoneGroupTopologyParser.Parse(wrongEnvelope, 4));
        Assert.Throws<FormatException>(() => ZoneGroupTopologyParser.Parse(wrongOutput, 4));
    }

    /// <summary>Rejects missing and malformed embedded topology XML.</summary>
    [Fact]
    public void RejectsEmptyOrMalformedEmbeddedXml()
    {
        Assert.Throws<FormatException>(() => ZoneGroupTopologyParser.Parse(this.Wrap(string.Empty), 4));
        Assert.Throws<FormatException>(() => ZoneGroupTopologyParser.Parse(this.Wrap("<ZoneGroups>"), 4));
    }

    /// <summary>Prohibits DTD processing in both the SOAP document and embedded topology.</summary>
    [Fact]
    public void RejectsDtdInEitherXmlLayer()
    {
        string outerDtd = "<!DOCTYPE Envelope [<!ENTITY x 'x'>]>" + this.Wrap(this.ValidTopology());
        string embeddedDtd = "<!DOCTYPE ZoneGroups [<!ENTITY x 'x'>]><ZoneGroups><ZoneGroup ID=\"g\" Coordinator=\"RINCON_123456789ABC01400\"><ZoneGroupMember UUID=\"RINCON_123456789ABC01400\" /></ZoneGroup></ZoneGroups>";

        Assert.Throws<FormatException>(() => ZoneGroupTopologyParser.Parse(outerDtd, 4));
        Assert.Throws<FormatException>(() => ZoneGroupTopologyParser.Parse(this.Wrap(embeddedDtd), 4));
    }

    /// <summary>Rejects missing or invalid group and device identifiers.</summary>
    /// <param name="groupId">Potentially invalid group identifier.</param>
    /// <param name="coordinator">Reported coordinator identifier.</param>
    /// <param name="memberId">Reported physical member identifier.</param>
    [Theory]
    [InlineData("", "RINCON_123456789ABC01400", "RINCON_123456789ABC01400")]
    [InlineData("g", "not-a-rincon", "not-a-rincon")]
    [InlineData("g", "RINCON_123456789ABC0140/", "RINCON_123456789ABC0140/")]
    public void RejectsInvalidIdentifiers(string groupId, string coordinator, string memberId)
    {
        string topology = $"<ZoneGroups><ZoneGroup ID=\"{groupId}\" Coordinator=\"{coordinator}\"><ZoneGroupMember UUID=\"{memberId}\" /></ZoneGroup></ZoneGroups>";

        Assert.Throws<FormatException>(() => ZoneGroupTopologyParser.Parse(this.Wrap(topology), 4));
    }

    /// <summary>Requires each coordinator to be one of its group’s direct members.</summary>
    [Fact]
    public void RejectsCoordinatorOnlyPresentAsSatellite()
    {
        string topology = $"<ZoneGroups><ZoneGroup ID=\"g\" Coordinator=\"{MEMBER_ID}\"><ZoneGroupMember UUID=\"{COORDINATOR_ID}\"><Satellite UUID=\"{MEMBER_ID}\" /></ZoneGroupMember></ZoneGroup></ZoneGroups>";

        Assert.Throws<FormatException>(() => ZoneGroupTopologyParser.Parse(this.Wrap(topology), 2));
    }

    /// <summary>Rejects duplicate group identifiers and duplicate member identifiers across groups.</summary>
    [Fact]
    public void RejectsDuplicateGroupAndMemberIdentifiers()
    {
        string duplicateGroup = $"<ZoneGroups><ZoneGroup ID=\"g\" Coordinator=\"{COORDINATOR_ID}\"><ZoneGroupMember UUID=\"{COORDINATOR_ID}\" /></ZoneGroup><ZoneGroup ID=\"g\" Coordinator=\"{MEMBER_ID}\"><ZoneGroupMember UUID=\"{MEMBER_ID}\" /></ZoneGroup></ZoneGroups>";
        string duplicateMember = $"<ZoneGroups><ZoneGroup ID=\"g1\" Coordinator=\"{COORDINATOR_ID}\"><ZoneGroupMember UUID=\"{COORDINATOR_ID}\" /></ZoneGroup><ZoneGroup ID=\"g2\" Coordinator=\"{MEMBER_ID}\"><ZoneGroupMember UUID=\"{COORDINATOR_ID}\" /><ZoneGroupMember UUID=\"{MEMBER_ID}\" /></ZoneGroup></ZoneGroups>";

        Assert.Throws<FormatException>(() => ZoneGroupTopologyParser.Parse(this.Wrap(duplicateGroup), 4));
        Assert.Throws<FormatException>(() => ZoneGroupTopologyParser.Parse(this.Wrap(duplicateMember), 4));
    }

    /// <summary>Counts nested satellites against the total member limit.</summary>
    [Fact]
    public void EnforcesMemberLimitIncludingSatellites()
    {
        string topology = $"<ZoneGroups><ZoneGroup ID=\"g\" Coordinator=\"{COORDINATOR_ID}\"><ZoneGroupMember UUID=\"{COORDINATOR_ID}\"><Satellite UUID=\"{MEMBER_ID}\" /></ZoneGroupMember></ZoneGroup></ZoneGroups>";

        Assert.Throws<FormatException>(() => ZoneGroupTopologyParser.Parse(this.Wrap(topology), 1));
        Assert.Equal(2, Assert.Single(ZoneGroupTopologyParser.Parse(this.Wrap(topology), 2)).Members.Count);
    }

    /// <summary>Wraps embedded topology text in the expected SOAP action response.</summary>
    /// <param name="topology">Embedded Sonos topology XML text.</param>
    /// <returns>A SOAP GetZoneGroupState response document.</returns>
    private string Wrap(string topology)
    {
        XNamespace soap = SOAP_NAMESPACE;
        XNamespace service = SERVICE_NAMESPACE;
        XDocument document = new(
            new XElement(
                soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "s", soap),
                new XAttribute(XNamespace.Xmlns + "u", service),
                new XElement(soap + "Body",
                    new XElement(service + "GetZoneGroupStateResponse",
                        new XElement("ZoneGroupState", topology)))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Creates the smallest valid topology used by rejection cases.</summary>
    /// <returns>A one-group embedded topology document.</returns>
    private string ValidTopology()
    {
        return $"<ZoneGroups><ZoneGroup ID=\"g\" Coordinator=\"{COORDINATOR_ID}\"><ZoneGroupMember UUID=\"{COORDINATOR_ID}\" /></ZoneGroup></ZoneGroups>";
    }
}
