using System.Xml;
using System.Xml.Linq;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Accepts only bounded, safely parsed Sonos player descriptions.</summary>
public static class PlayerDescriptionParser
{
    /// <summary>Parses a UPnP description after verifying Sonos identity and topology service.</summary>
    /// <param name="xml">The bounded XML body.</param>
    /// <returns>Validated player metadata.</returns>
    /// <exception cref="FormatException">The document is malformed or does not identify a Sonos player.</exception>
    public static PlayerDescription Parse(string xml)
    {
        try
        {
            using StringReader input = new(xml);
            using XmlReader reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 256 * 1024,
                MaxCharactersFromEntities = 0
            });
            XDocument document = XDocument.Load(reader);
            XElement? device = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "device");
            if (device is null)
            {
                throw new FormatException("Missing UPnP device.");
            }

            string? manufacturer = ReadValue(device, "manufacturer");
            string? deviceType = ReadValue(device, "deviceType");
            string? udn = ReadValue(device, "UDN");
            bool topology = device.Descendants().Any(element =>
                element.Name.LocalName == "serviceType" &&
                element.Value.Contains("ZoneGroupTopology", StringComparison.OrdinalIgnoreCase));
            if (manufacturer?.Contains("Sonos", StringComparison.OrdinalIgnoreCase) != true ||
                deviceType?.Contains("ZonePlayer", StringComparison.OrdinalIgnoreCase) != true ||
                topology == false || udn is null ||
                udn.StartsWith("uuid:RINCON_", StringComparison.OrdinalIgnoreCase) == false ||
                udn.Length > 128)
            {
                throw new FormatException("Description is not a supported Sonos player.");
            }

            string id = udn[5..].ToUpperInvariant();
            if (id.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_') == false)
            {
                throw new FormatException("Invalid player UDN.");
            }

            return new PlayerDescription(id, ReadValue(device, "roomName"), ReadValue(device, "modelName"))
            {
                SoftwareVersion = TechnicalValue(device, "softwareVersion"),
                HardwareVersion = TechnicalValue(device, "hardwareVersion"),
                ModelNumber = TechnicalValue(device, "modelNumber"),
                SupportsPlayback = SupportsControl(device, "AVTransport", "/MediaRenderer/AVTransport/Control"),
                SupportsVolume = SupportsControl(device, "RenderingControl", "/MediaRenderer/RenderingControl/Control"),
                // Support is explicit; arbitrary advertised event URLs never become outbound destinations.
                SupportsTopologyEvents = device.Descendants().Where(element => element.Name.LocalName == "service")
                    .Any(service => ReadValue(service, "serviceType") == "urn:schemas-upnp-org:service:ZoneGroupTopology:1" &&
                        ReadValue(service, "eventSubURL") is "/ZoneGroupTopology/Event" or "ZoneGroupTopology/Event")
            };
        }
        catch (XmlException exception)
        {
            throw new FormatException("Malformed device description.", exception);
        }
    }

    /// <summary>Checks an advertised service against an application-owned local path.</summary>
    /// <param name="device">Device element.</param>
    /// <param name="serviceName">Known service name.</param>
    /// <param name="path">Fixed supported local path.</param>
    private static bool SupportsControl(XElement device, string serviceName, string path) =>
        device.Descendants().Where(element => element.Name.LocalName == "service")
            .Any(service => ReadValue(service, "serviceType") == $"urn:schemas-upnp-org:service:{serviceName}:1" &&
                ReadValue(service, "controlURL") is string control && (control == path || control == path[1..]));

    /// <summary>Reads bounded technical metadata without accepting device-supplied identifiers as labels.</summary>
    /// <param name="device">Device element.</param>
    /// <param name="name">Known metadata field.</param>
    private static string? TechnicalValue(XElement device, string name)
    {
        string? value = ReadValue(device, name);
        return value?.Length > 128 ? throw new FormatException("Technical metadata exceeds its size limit.") : value;
    }

    /// <summary>Reads a direct device child by its local XML name.</summary>
    /// <param name="device">Device element.</param>
    /// <param name="name">Child local name.</param>
    /// <returns>Trimmed value or null.</returns>
    private static string? ReadValue(XElement device, string name)
    {
        string? value = device.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
