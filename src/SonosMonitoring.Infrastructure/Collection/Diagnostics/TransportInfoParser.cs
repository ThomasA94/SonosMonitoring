using System.Xml;
using System.Xml.Linq;
using SonosMonitoring.Application.Collection.Diagnostics;

namespace SonosMonitoring.Infrastructure.Collection.Diagnostics;

/// <summary>Parses read-only AVTransport GetTransportInfo SOAP results.</summary>
public static class TransportInfoParser
{
    private const string SOAP_NAMESPACE = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string SERVICE_NAMESPACE = "urn:schemas-upnp-org:service:AVTransport:1";
    private const int MAX_DOCUMENT_CHARACTERS = 64 * 1024;
    private const int MAX_XML_ELEMENTS = 128;
    private const int MAX_XML_DEPTH = 16;
    private const int MAX_ENUM_LENGTH = 64;

    /// <summary>Reads the current state and optional status from a GetTransportInfo response.</summary>
    /// <param name="xml">The bounded SOAP response body.</param>
    /// <returns>The reported transport state and status.</returns>
    /// <exception cref="FormatException">The response is malformed, incomplete, or exceeds supported bounds.</exception>
    public static PlaybackObservation Parse(string xml)
    {
        XDocument document = TransportInfoParser.ReadDocument(xml);
        XNamespace soap = SOAP_NAMESPACE;
        XNamespace service = SERVICE_NAMESPACE;
        XElement? envelope = document.Root;
        XElement? body = envelope?.Name == soap + "Envelope" ? envelope.Element(soap + "Body") : null;
        XElement? response = body?.Element(service + "GetTransportInfoResponse");
        if (envelope is null || envelope.Elements(soap + "Body").Count() != 1 || body is null || body.Elements().Count() != 1 || response is null)
        {
            throw new FormatException("Missing AVTransport SOAP response.");
        }

        XElement[] states = response.Elements("CurrentTransportState").ToArray();
        XElement[] statuses = response.Elements("CurrentTransportStatus").ToArray();
        XElement[] speeds = response.Elements("CurrentSpeed").ToArray();
        if (states.Length != 1 || statuses.Length > 1 || speeds.Length != 1 || response.Elements().Count() is < 2 or > 3 ||
            states[0].HasElements || statuses.Any(element => element.HasElements) || speeds[0].HasElements ||
            speeds[0].Value.Length is 0 or > 16)
        {
            throw new FormatException("The AVTransport response is incomplete or invalid.");
        }

        string state = TransportInfoParser.ReadEnumeration(states[0].Value);
        string? status = statuses.Length == 0 ? null : TransportInfoParser.ReadEnumeration(statuses[0].Value);
        return new PlaybackObservation(state, status);
    }

    /// <summary>Validates and returns a bounded enum-like protocol value.</summary>
    /// <param name="value">The scalar protocol value.</param>
    /// <returns>The unchanged protocol value.</returns>
    private static string ReadEnumeration(string value)
    {
        if (value.Length is 0 or > MAX_ENUM_LENGTH || value.Any(character => char.IsAsciiLetterOrDigit(character) == false && character != '_'))
        {
            throw new FormatException("An AVTransport enumeration value is invalid.");
        }

        return value;
    }

    /// <summary>Loads bounded XML with DTD and external entity processing disabled.</summary>
    /// <param name="xml">The XML response text.</param>
    /// <returns>The validated XML document.</returns>
    /// <exception cref="FormatException">The XML is malformed or exceeds resource limits.</exception>
    private static XDocument ReadDocument(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MAX_DOCUMENT_CHARACTERS)
        {
            throw new FormatException("The AVTransport response is empty or exceeds the supported size.");
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
            TransportInfoParser.CheckStructure(document);
            return document;
        }
        catch (XmlException exception)
        {
            throw new FormatException("Malformed AVTransport XML.", exception);
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
                throw new FormatException("The AVTransport response exceeds structural limits.");
            }
        }
    }
}
