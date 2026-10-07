using System.Xml;
using System.Xml.Linq;
using SonosMonitoring.Application.Collection;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Parses bounded SOAP topology observations without following advertised URLs.</summary>
public static class ZoneGroupTopologyParser
{
    private const string SOAP_NAMESPACE = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string SERVICE_NAMESPACE = "urn:schemas-upnp-org:service:ZoneGroupTopology:1";

    /// <summary>Validates the SOAP result and its embedded XML as one complete observation.</summary>
    /// <param name="xml">Bounded SOAP response body.</param>
    /// <param name="maxMembers">Maximum physical members across all groups, including satellites.</param>
    /// <returns>Reported groups with stable member identities and no inferred speaker roles.</returns>
    /// <exception cref="FormatException">The payload is malformed, inconsistent, or exceeds structural limits.</exception>
    public static IReadOnlyList<TopologyGroup> Parse(string xml, int maxMembers)
    {
        try
        {
            XNamespace soap = SOAP_NAMESPACE;
            XNamespace service = SERVICE_NAMESPACE;
            XElement envelope = ReadXml(xml);
            XElement? response = envelope.Name == soap + "Envelope"
                ? envelope.Element(soap + "Body")?.Element(service + "GetZoneGroupStateResponse") : null;
            XElement[] outputs = response?.Elements("ZoneGroupState").ToArray() ?? [];
            if (outputs.Length != 1 || outputs[0].HasElements)
            {
                throw new FormatException("Missing topology action response.");
            }

            XElement root = ReadXml(outputs[0].Value);
            XElement? groups = root.Name == "ZoneGroups" ? root :
                root.Name == "ZoneGroupState" ? root.Element("ZoneGroups") ?? root : null;
            if (groups is null)
            {
                throw new FormatException("Missing topology groups.");
            }

            List<TopologyGroup> result = [];
            HashSet<string> groupIds = new(StringComparer.Ordinal);
            HashSet<string> memberIds = new(StringComparer.Ordinal);
            foreach (XElement group in groups.Elements("ZoneGroup"))
            {
                string groupId = RequiredAttribute(group, "ID");
                string coordinator = DeviceId(RequiredAttribute(group, "Coordinator"));
                if (groupId.Length > 256 || groupIds.Add(groupId) == false)
                {
                    throw new FormatException("Invalid or repeated group identifier.");
                }

                List<TopologyMember> members = [];
                foreach (XElement member in group.Elements("ZoneGroupMember"))
                {
                    TopologyMember parsed = ReadMember(member, null, memberIds, maxMembers);
                    members.Add(parsed);
                    foreach (XElement satellite in member.Elements("Satellite"))
                    {
                        members.Add(ReadMember(satellite, parsed.DeviceId, memberIds, maxMembers));
                    }
                }

                if (members.Any(member => member.DeviceId == coordinator && member.ParentDeviceId is null) == false)
                {
                    throw new FormatException("Coordinator is not a direct group member.");
                }

                result.Add(new TopologyGroup(groupId, coordinator, members.ToArray()));
            }

            if (result.Count == 0)
            {
                throw new FormatException("No topology groups were reported.");
            }

            return result.ToArray();
        }
        catch (XmlException exception)
        {
            throw new FormatException("Malformed topology XML.", exception);
        }
    }

    /// <summary>Reads one physical member and enforces a shared bound across the observation.</summary>
    /// <param name="element">Member or satellite element.</param>
    /// <param name="parent">Enclosing member identity, if any.</param>
    /// <param name="identities">Identities already seen in this response.</param>
    /// <param name="maxMembers">Maximum total physical members.</param>
    /// <returns>A member preserving only supported attributes.</returns>
    private static TopologyMember ReadMember(XElement element, string? parent, HashSet<string> identities, int maxMembers)
    {
        string id = DeviceId(RequiredAttribute(element, "UUID"));
        if (identities.Add(id) == false || identities.Count > maxMembers)
        {
            throw new FormatException("Repeated member or topology capacity exceeded.");
        }

        bool? invisible = element.Attribute("Invisible")?.Value switch
        {
            null => null,
            "0" => false,
            "1" => true,
            _ => throw new FormatException("Invalid member visibility flag.")
        };
        string? name = element.Attribute("ZoneName")?.Value;
        if (name?.Length > 256)
        {
            throw new FormatException("Zone name exceeds the supported length.");
        }

        return new TopologyMember(id, name, invisible, parent)
        {
            ReportedAddress = PlayerDescriptionLocation.GetAddress(element.Attribute("Location")?.Value)
        };
    }

    /// <summary>Canonicalizes the same UDN character set used for device descriptions.</summary>
    /// <param name="value">Reported member or coordinator UDN without a prefix.</param>
    /// <returns>Uppercase stable device identity.</returns>
    private static string DeviceId(string value)
    {
        string id = value.ToUpperInvariant();
        if (id.StartsWith("RINCON_", StringComparison.Ordinal) == false || id.Length > 128 ||
            id.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_') == false)
        {
            throw new FormatException("Invalid topology member identity.");
        }

        return id;
    }

    /// <summary>Requires a nonempty attribute rather than inventing missing identity.</summary>
    /// <param name="element">Owning element.</param>
    /// <param name="name">Required attribute name.</param>
    /// <returns>The reported value.</returns>
    private static string RequiredAttribute(XElement element, string name)
    {
        string? value = element.Attribute(name)?.Value;
        return string.IsNullOrWhiteSpace(value) ? throw new FormatException("Missing topology attribute.") : value;
    }

    /// <summary>Disables external entities and DTDs for both layers of XML.</summary>
    /// <param name="xml">Bounded XML text.</param>
    /// <returns>The root element.</returns>
    private static XElement ReadXml(string xml)
    {
        using StringReader input = new(xml);
        using XmlReader reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1024 * 1024,
            MaxCharactersFromEntities = 0
        });
        return XElement.Load(reader);
    }
}
