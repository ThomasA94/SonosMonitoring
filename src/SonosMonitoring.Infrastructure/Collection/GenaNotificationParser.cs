using System.Xml;
using System.Xml.Linq;
using System.Text;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Validates a bounded UPnP event property set and detects topology changes.</summary>
public static class GenaNotificationParser
{
    private const string EVENT_NAMESPACE = "urn:schemas-upnp-org:event-1-0";
    private const int MAXIMUM_XML_CHARACTERS = 1024 * 1024;

    /// <summary>Validates notification XML and reports whether it contains ZoneGroupState.</summary>
    /// <param name="xml">The UTF-8-decoded, bounded notification body.</param>
    /// <returns><see langword="true"/> when a valid property set contains ZoneGroupState.</returns>
    /// <exception cref="FormatException">The payload is oversized, malformed, or structurally invalid.</exception>
    public static bool Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        if (Encoding.UTF8.GetByteCount(xml) > MAXIMUM_XML_CHARACTERS)
        {
            throw new FormatException("The GENA notification exceeds the supported size.");
        }

        try
        {
            using StringReader input = new(xml);
            using XmlReader reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MAXIMUM_XML_CHARACTERS,
                MaxCharactersFromEntities = 0
            });

            XElement root = XElement.Load(reader, LoadOptions.None);
            XNamespace eventNamespace = EVENT_NAMESPACE;
            if (root.Name != eventNamespace + "propertyset")
            {
                throw new FormatException("The GENA notification has an invalid property set root.");
            }

            bool hasTopology = false;
            foreach (XElement property in root.Elements())
            {
                XElement[] values = property.Elements().ToArray();
                if (property.Name != eventNamespace + "property" || values.Length != 1 ||
                    values[0].HasElements || property.Nodes().OfType<XElement>().Count() != 1)
                {
                    throw new FormatException("The GENA notification contains an invalid property.");
                }

                if (values[0].Name.NamespaceName.Length != 0)
                {
                    throw new FormatException("The GENA property value name must be unqualified.");
                }

                hasTopology |= values[0].Name == "ZoneGroupState";
            }

            return hasTopology;
        }
        catch (XmlException exception)
        {
            throw new FormatException("The GENA notification XML is malformed.", exception);
        }
    }
}
