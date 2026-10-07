using System.Xml;
using System.Xml.Linq;
using SonosMonitoring.Application.Collection.Diagnostics;

namespace SonosMonitoring.Infrastructure.Collection.Diagnostics;

/// <summary>Parses the supported fields from a Sonos wireless status document.</summary>
public static class WirelessStatusParser
{
    private const int MAX_DOCUMENT_CHARACTERS = 64 * 1024;
    private const int MAX_XML_ELEMENTS = 128;
    private const int MAX_XML_DEPTH = 16;
    private const int MAX_ENUM_LENGTH = 64;

    /// <summary>Reads wireless values while preserving absent optional values as null.</summary>
    /// <param name="xml">The bounded /status/wireless response body.</param>
    /// <returns>Wireless values reported by the device.</returns>
    /// <exception cref="FormatException">The document is malformed, incomplete, or exceeds supported bounds.</exception>
    public static WirelessObservation Parse(string xml)
    {
        XDocument document = WirelessStatusParser.ReadDocument(xml);
        XElement root = document.Root ?? throw new FormatException("Missing wireless document root.");
        if (root.Name != "ZPSupportInfo" || root.Elements().Count() != 1 || root.Element("Wireless") is not XElement wireless ||
            wireless.Elements().Count() != 1 || wireless.Element("WirelessInfo") is not XElement info)
        {
            throw new FormatException("Unexpected wireless document structure.");
        }

        string? wifiMode = WirelessStatusParser.OptionalEnum(info, "WifiModeString");
        string? connectionType = WirelessStatusParser.OptionalEnum(info, "ConnectionTypeString");
        if (wifiMode is null && connectionType is null)
        {
            throw new FormatException("The wireless response contains no supported mode or connection type.");
        }

        bool? sonosNetDisabled = WirelessStatusParser.OptionalFlag(info, "SonosNetDisabled");
        return new WirelessObservation(wifiMode, connectionType, sonosNetDisabled);
    }

    /// <summary>Reads an optional bounded enumeration string and rejects duplicate or malformed values.</summary>
    /// <param name="parent">The element containing the value.</param>
    /// <param name="name">The expected child element name.</param>
    /// <returns>The value, or null when the element is absent.</returns>
    private static string? OptionalEnum(XElement parent, string name)
    {
        XElement[] matches = parent.Elements(name).ToArray();
        if (matches.Length > 1)
        {
            throw new FormatException("A wireless field occurs more than once.");
        }

        if (matches.Length == 0)
        {
            return null;
        }

        string value = matches[0].Value;
        if (matches[0].HasElements || value.Length is 0 or > MAX_ENUM_LENGTH ||
            value.Any(character => char.IsAsciiLetterOrDigit(character) == false && character != '_'))
        {
            throw new FormatException("A wireless enumeration value is invalid.");
        }

        return value;
    }

    /// <summary>Reads an optional zero-or-one flag without treating absence as false.</summary>
    /// <param name="parent">The element containing the flag.</param>
    /// <param name="name">The expected child element name.</param>
    /// <returns>The parsed flag, or null when the element is absent.</returns>
    private static bool? OptionalFlag(XElement parent, string name)
    {
        XElement[] matches = parent.Elements(name).ToArray();
        if (matches.Length > 1)
        {
            throw new FormatException("A wireless field occurs more than once.");
        }

        if (matches.Length == 0)
        {
            return null;
        }

        if (matches[0].HasElements)
        {
            throw new FormatException("The SonosNetDisabled flag is invalid.");
        }

        return matches[0].Value switch
        {
            "0" => false,
            "1" => true,
            _ => throw new FormatException("The SonosNetDisabled flag is invalid.")
        };
    }

    /// <summary>Loads a bounded XML document with DTD and external entity processing disabled.</summary>
    /// <param name="xml">The XML response text.</param>
    /// <returns>The validated XML document.</returns>
    /// <exception cref="FormatException">The XML is malformed or exceeds resource limits.</exception>
    private static XDocument ReadDocument(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MAX_DOCUMENT_CHARACTERS)
        {
            throw new FormatException("The wireless response is empty or exceeds the supported size.");
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
            WirelessStatusParser.CheckStructure(document);
            return document;
        }
        catch (XmlException exception)
        {
            throw new FormatException("Malformed wireless XML.", exception);
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
                throw new FormatException("The wireless response exceeds structural limits.");
            }
        }
    }
}
