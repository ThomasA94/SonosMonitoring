using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using SonosMonitoring.Application.Collection.Diagnostics;

namespace SonosMonitoring.Infrastructure.Collection.Diagnostics;

/// <summary>Parses link observations from a Sonos Ethernet ports document.</summary>
public static class EthernetPortsParser
{
    private const int MAX_DOCUMENT_CHARACTERS = 64 * 1024;
    private const int MAX_XML_ELEMENTS = 128;
    private const int MAX_XML_DEPTH = 16;
    private const int MAX_PORTS = 32;

    /// <summary>Reads bounded port entries without assigning a unit to the reported speed.</summary>
    /// <param name="xml">The bounded /status/enetports response body.</param>
    /// <returns>Reported port identifiers, link flags, and raw speed values.</returns>
    /// <exception cref="FormatException">The document is malformed, incomplete, or exceeds supported bounds.</exception>
    public static IReadOnlyList<EthernetPortObservation> Parse(string xml)
    {
        XDocument document = EthernetPortsParser.ReadDocument(xml);
        XElement root = document.Root ?? throw new FormatException("Missing Ethernet document root.");
        XElement? ports = root.Name == "ZPSupportInfo" ? root.Element("EnetPorts") : null;
        XElement[] entries = ports?.Elements("Port").ToArray() ?? [];
        if (root.Name != "ZPSupportInfo" || root.Elements().Count() != 1 || ports is null ||
            entries.Length is 0 or > MAX_PORTS)
        {
            throw new FormatException("Missing or invalid Ethernet ports.");
        }

        List<EthernetPortObservation> observations = [];
        HashSet<string> identifiers = new(StringComparer.Ordinal);
        foreach (XElement entry in entries)
        {
            string? port = entry.Attribute("port")?.Value;
            XElement[] links = entry.Elements("Link").ToArray();
            XElement[] speeds = entry.Elements("Speed").ToArray();
            if (string.IsNullOrEmpty(port) || port.Length > 32 || port.Any(character => char.IsAsciiDigit(character) == false) ||
                identifiers.Add(port) == false || links.Length != 1 || speeds.Length > 1 || entry.Elements().Count() is < 1 or > 2 ||
                links[0].HasElements || speeds.Any(element => element.HasElements))
            {
                throw new FormatException("An Ethernet port entry is incomplete or invalid.");
            }

            bool linkUp = links[0].Value switch
            {
                "0" => false,
                "1" => true,
                _ => throw new FormatException("An Ethernet link flag is invalid.")
            };
            int? speed = null;
            if (speeds.Length == 1)
            {
                if (int.TryParse(speeds[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedSpeed) == false || parsedSpeed < 0)
                {
                    throw new FormatException("An Ethernet speed value is invalid.");
                }

                speed = parsedSpeed;
            }

            observations.Add(new EthernetPortObservation(port, linkUp, speed));
        }

        return observations.ToArray();
    }

    /// <summary>Loads bounded XML with DTD and external entity processing disabled.</summary>
    /// <param name="xml">The XML response text.</param>
    /// <returns>The validated XML document.</returns>
    /// <exception cref="FormatException">The XML is malformed or exceeds resource limits.</exception>
    private static XDocument ReadDocument(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MAX_DOCUMENT_CHARACTERS)
        {
            throw new FormatException("The Ethernet response is empty or exceeds the supported size.");
        }

        try
        {
            using StringReader input = new(xml);
            using XmlReader reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MAX_DOCUMENT_CHARACTERS,
                MaxCharactersFromEntities = 0
            });
            XDocument document = XDocument.Load(reader, LoadOptions.None);
            EthernetPortsParser.CheckStructure(document);
            return document;
        }
        catch (XmlException exception)
        {
            throw new FormatException("Malformed Ethernet XML.", exception);
        }
    }

    /// <summary>Checks XML element count and nesting depth before field extraction.</summary>
    /// <param name="document">The parsed document.</param>
    private static void CheckStructure(XDocument document)
    {
        int elementCount = 0;
        foreach (XElement element in document.Descendants())
        {
            elementCount++;
            if (elementCount > MAX_XML_ELEMENTS || element.Ancestors().Count() > MAX_XML_DEPTH)
            {
                throw new FormatException("The Ethernet response exceeds structural limits.");
            }
        }
    }
}
