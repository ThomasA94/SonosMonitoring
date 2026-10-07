using System.Xml.Linq;

namespace SonosMonitoring.Infrastructure.MediaControl.Transport;

/// <summary>Parses bounded DIDL-Lite browse results while retaining media metadata privately.</summary>
public static class MediaBrowseParser
{
    /// <summary>Parses one DIDL page and checks its reported result count.</summary>
    /// <param name="result">Escaped DIDL-Lite result.</param>
    /// <param name="numberReturned">Device-reported count.</param>
    /// <param name="limit">Requested maximum.</param>
    /// <returns>Private object identifiers, playable URIs, metadata, and display fields.</returns>
    public static IReadOnlyList<MediaBrowseEntry> Parse(string result, int numberReturned, int limit)
    {
        if (numberReturned < 0 || numberReturned > limit || limit is < 1 or > 100)
        {
            throw new FormatException("Invalid browse count.");
        }
        if (numberReturned == 0 && string.IsNullOrWhiteSpace(result))
        {
            return [];
        }

        XDocument document = MediaSoapProtocol.ReadDocument(result);
        if (document.Root?.Name.LocalName != "DIDL-Lite")
        {
            throw new FormatException("Invalid DIDL root.");
        }
        XElement[] objects = document.Root.Elements().ToArray();
        if (objects.Length != numberReturned || objects.Length > 100 ||
            objects.Any(element => element.Name.LocalName is not ("item" or "container")))
        {
            throw new FormatException("DIDL result count or object type is invalid.");
        }

        List<MediaBrowseEntry> entries = [];
        foreach (XElement item in objects)
        {
            string id = item.Attribute("id")?.Value ?? string.Empty;
            string title = item.Descendants().FirstOrDefault(element => element.Name.LocalName == "title")?.Value ?? string.Empty;
            string? artist = item.Descendants().FirstOrDefault(element => element.Name.LocalName == "creator")?.Value;
            string? album = item.Descendants().FirstOrDefault(element => element.Name.LocalName == "album")?.Value;
            string? uri = item.Elements().FirstOrDefault(element => element.Name.LocalName == "res")?.Value;
            if (id.Length is < 1 or > 256 || title.Length is < 1 or > 512 || artist?.Length > 512 ||
                album?.Length > 512 || uri?.Length > 2048)
            {
                throw new FormatException("DIDL metadata exceeds supported bounds.");
            }
            string? resourceMetadata = item.Elements().FirstOrDefault(element => element.Name.LocalName == "resMD")?.Value;
            string metadata;
            if (string.IsNullOrWhiteSpace(resourceMetadata))
            {
                metadata = new XDocument(new XElement(document.Root.Name, document.Root.Attributes(),
                    new XElement(item))).ToString(SaveOptions.DisableFormatting);
            }
            else
            {
                XDocument resourceDocument = MediaSoapProtocol.ReadDocument(resourceMetadata);
                if (resourceDocument.Root?.Name.LocalName != "DIDL-Lite")
                {
                    throw new FormatException("Invalid favorite playback metadata.");
                }
                metadata = resourceMetadata;
            }
            entries.Add(new MediaBrowseEntry(id, title, artist, album, uri, metadata));
        }
        return entries;
    }
}
