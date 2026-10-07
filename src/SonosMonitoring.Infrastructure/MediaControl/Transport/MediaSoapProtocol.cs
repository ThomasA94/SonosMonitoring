using System.Xml;
using System.Xml.Linq;

namespace SonosMonitoring.Infrastructure.MediaControl.Transport;

/// <summary>Creates and parses only bounded, fixed-service UPnP SOAP messages.</summary>
public static class MediaSoapProtocol
{
    private const string SOAP_NAMESPACE = "http://schemas.xmlsoap.org/soap/envelope/";
    private const int MAX_XML_CHARACTERS = 256 * 1024;

    /// <summary>Builds a SOAP envelope for an application-whitelisted service and action.</summary>
    /// <param name="service">Known service name.</param>
    /// <param name="action">Known action name.</param>
    /// <param name="arguments">Ordered protocol arguments.</param>
    /// <returns>Encoded SOAP XML.</returns>
    public static string Build(string service, string action, IReadOnlyDictionary<string, string> arguments)
    {
        if (MediaSoapProtocol.IsAllowed(service, action) == false)
        {
            throw new ArgumentException("Unsupported local SOAP action.");
        }

        XNamespace soap = SOAP_NAMESPACE;
        XNamespace protocol = $"urn:schemas-upnp-org:service:{service}:1";
        XElement call = new(protocol + action, arguments.Select(argument => new XElement(argument.Key, argument.Value)));
        return new XDocument(new XElement(soap + "Envelope", new XAttribute(XNamespace.Xmlns + "s", soap),
            new XElement(soap + "Body", call))).ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Reads a matching response and preserves a SOAP fault even on HTTP 500.</summary>
    /// <param name="xml">Bounded response body.</param>
    /// <param name="service">Expected service.</param>
    /// <param name="action">Expected action.</param>
    /// <returns>Action output fields.</returns>
    public static IReadOnlyDictionary<string, string> Parse(string xml, string service, string action)
    {
        XDocument document = MediaSoapProtocol.ReadDocument(xml);
        XNamespace soap = SOAP_NAMESPACE;
        XElement? body = document.Root?.Name == soap + "Envelope" ? document.Root.Element(soap + "Body") : null;
        if (body is null || body.Elements().Count() != 1)
        {
            throw new FormatException("Invalid SOAP envelope.");
        }

        XElement result = body.Elements().Single();
        if (result.Name == soap + "Fault")
        {
            string? code = result.Descendants().FirstOrDefault(element => element.Name.LocalName == "errorCode")?.Value;
            throw new MediaSoapFaultException(code is { Length: > 0 and <= 8 } &&
                code.All(char.IsAsciiDigit) ? code : "unknown");
        }

        if (result.Name != XName.Get(action + "Response", $"urn:schemas-upnp-org:service:{service}:1") ||
            result.Elements().Count() > 64 || result.Descendants().Count() > 128)
        {
            throw new FormatException("Unexpected SOAP action response.");
        }

        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        foreach (XElement field in result.Elements())
        {
            if (field.HasElements || fields.TryAdd(field.Name.LocalName, field.Value) == false)
            {
                throw new FormatException("Invalid SOAP output field.");
            }
        }

        return fields;
    }

    /// <summary>Loads XML with DTD, external entities, and excessive nesting disabled.</summary>
    /// <param name="xml">Untrusted XML.</param>
    /// <returns>Validated document.</returns>
    public static XDocument ReadDocument(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MAX_XML_CHARACTERS)
        {
            throw new FormatException("XML is empty or exceeds its size limit.");
        }

        try
        {
            using StringReader input = new(xml);
            using XmlReader reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MAX_XML_CHARACTERS,
                MaxCharactersFromEntities = 0
            });
            XDocument document = XDocument.Load(reader, LoadOptions.None);
            int count = 0;
            foreach (XElement element in document.Descendants())
            {
                if (++count > 2048 || element.Ancestors().Count() > 24)
                {
                    throw new FormatException("XML structure exceeds its limit.");
                }
            }

            return document;
        }
        catch (XmlException exception)
        {
            throw new FormatException("Malformed XML.", exception);
        }
    }

    /// <summary>Restricts all outbound SOAP actions to a fixed local protocol surface.</summary>
    /// <param name="service">Service name.</param>
    /// <param name="action">Action name.</param>
    /// <returns>Whether the exact pair is supported.</returns>
    public static bool IsAllowed(string service, string action) => service switch
    {
        "AVTransport" => action is "GetTransportInfo" or "GetMediaInfo" or "GetPositionInfo" or
            "GetTransportSettings" or "GetCrossfadeMode" or "GetCurrentTransportActions" or "Play" or
            "Pause" or "Stop" or "Next" or "Previous" or "Seek" or "SetPlayMode" or
            "SetCrossfadeMode" or "SetAVTransportURI" or "AddURIToQueue" or "RemoveTrackRangeFromQueue" or
            "ReorderTracksInQueue" or "RemoveAllTracksFromQueue" or "BecomeCoordinatorOfStandaloneGroup",
        "RenderingControl" => action is "GetVolume" or "GetMute" or "GetOutputFixed" or "SetVolume" or "SetMute",
        "GroupRenderingControl" => action is "GetGroupVolume" or "GetGroupMute" or "SnapshotGroupVolume" or
            "SetGroupVolume" or "SetGroupMute",
        "ContentDirectory" => action is "Browse",
        "DeviceProperties" => action is "GetHouseholdID",
        "ZoneGroupTopology" => action is "GetZoneGroupState",
        _ => false
    };
}
