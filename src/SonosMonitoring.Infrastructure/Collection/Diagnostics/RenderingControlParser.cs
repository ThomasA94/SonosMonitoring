using System.Xml;
using System.Xml.Linq;

namespace SonosMonitoring.Infrastructure.Collection.Diagnostics;

/// <summary>Parses read-only RenderingControl SOAP results.</summary>
public static class RenderingControlParser
{
    private const string SOAP_NAMESPACE = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string SERVICE_NAMESPACE = "urn:schemas-upnp-org:service:RenderingControl:1";
    private const int MAX_DOCUMENT_CHARACTERS = 64 * 1024;
    private const int MAX_XML_ELEMENTS = 128;
    private const int MAX_XML_DEPTH = 16;

    /// <summary>Reads the Master channel volume from a GetVolume SOAP response.</summary>
    /// <param name="xml">The bounded SOAP response body.</param>
    /// <returns>The reported volume from 0 through 100.</returns>
    /// <exception cref="FormatException">The response is malformed, incomplete, or outside supported bounds.</exception>
    public static int ParseVolume(string xml)
    {
        XElement value = RenderingControlParser.ReadActionOutput(xml, "GetVolumeResponse", "CurrentVolume");
        string? channel = value.Attribute("channel")?.Value;
        if (channel is not null && channel != "Master")
        {
            throw new FormatException("The response does not report the Master channel.");
        }

        if (int.TryParse(value.Value, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out int volume) == false || volume is < 0 or > 100)
        {
            throw new FormatException("The reported volume is invalid.");
        }

        return volume;
    }

    /// <summary>Reads the Master channel mute flag from a GetMute SOAP response.</summary>
    /// <param name="xml">The bounded SOAP response body.</param>
    /// <returns><see langword="true"/> when mute is enabled; otherwise <see langword="false"/>.</returns>
    /// <exception cref="FormatException">The response is malformed, incomplete, or outside supported bounds.</exception>
    public static bool ParseMute(string xml)
    {
        XElement value = RenderingControlParser.ReadActionOutput(xml, "GetMuteResponse", "CurrentMute");
        string? channel = value.Attribute("channel")?.Value;
        if (channel is not null && channel != "Master")
        {
            throw new FormatException("The response does not report the Master channel.");
        }

        return value.Value switch
        {
            "0" => false,
            "1" => true,
            _ => throw new FormatException("The reported mute flag is invalid.")
        };
    }

    /// <summary>Validates the SOAP envelope and extracts exactly one action output element.</summary>
    /// <param name="xml">The bounded SOAP response body.</param>
    /// <param name="actionName">The expected response action name.</param>
    /// <param name="outputName">The expected scalar output name.</param>
    /// <returns>The single scalar output element.</returns>
    /// <exception cref="FormatException">The document is malformed or has an unexpected structure.</exception>
    private static XElement ReadActionOutput(string xml, string actionName, string outputName)
    {
        XDocument document = RenderingControlParser.ReadDocument(xml);
        XNamespace soap = SOAP_NAMESPACE;
        XNamespace service = SERVICE_NAMESPACE;
        XElement? envelope = document.Root;
        XElement? body = envelope?.Name == soap + "Envelope" ? envelope.Element(soap + "Body") : null;
        XElement? response = body?.Element(service + actionName);
        XElement? output = response?.Element(outputName);
        if (envelope is null || envelope.Elements(soap + "Body").Count() != 1 || body is null || body.Elements().Count() != 1 || response is null ||
            response.Elements().Count() != 1 || output is null || output.HasElements || output.Value.Length > 16)
        {
            throw new FormatException("Missing or invalid SOAP action output.");
        }

        return output;
    }

    /// <summary>Loads a small XML document with DTD and external entity processing disabled.</summary>
    /// <param name="xml">The XML response text.</param>
    /// <returns>The validated XML document.</returns>
    /// <exception cref="FormatException">The XML is malformed or exceeds resource limits.</exception>
    private static XDocument ReadDocument(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MAX_DOCUMENT_CHARACTERS)
        {
            throw new FormatException("The SOAP response is empty or exceeds the supported size.");
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
            int elementCount = 0;
            foreach (XElement element in document.Descendants())
            {
                elementCount++;
                if (elementCount > MAX_XML_ELEMENTS || element.Ancestors().Count() > MAX_XML_DEPTH)
                {
                    throw new FormatException("The SOAP response exceeds structural limits.");
                }
            }

            return document;
        }
        catch (XmlException exception)
        {
            throw new FormatException("Malformed SOAP XML.", exception);
        }
    }
}
