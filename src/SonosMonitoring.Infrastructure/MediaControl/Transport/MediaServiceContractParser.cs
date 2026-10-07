using System.Xml.Linq;

namespace SonosMonitoring.Infrastructure.MediaControl.Transport;

/// <summary>Reads exact action names from a bounded local UPnP service description.</summary>
public static class MediaServiceContractParser
{
    /// <summary>Parses the advertised action list with structural and name limits.</summary>
    /// <param name="xml">Bounded SCPD XML.</param>
    /// <returns>Exact advertised action names.</returns>
    public static IReadOnlySet<string> ParseActions(string xml)
    {
        XDocument document = MediaSoapProtocol.ReadDocument(xml);
        XElement? root = document.Root;
        XElement? list = root?.Elements().SingleOrDefault(element => element.Name.LocalName == "actionList");
        if (root?.Name.LocalName != "scpd" || list is null)
        {
            throw new FormatException("Invalid service contract.");
        }
        XElement[] actions = list.Elements().ToArray();
        if (actions.Length is < 1 or > 128 || actions.Any(element => element.Name.LocalName != "action"))
        {
            throw new FormatException("Invalid service action list.");
        }
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (XElement action in actions)
        {
            string? name = action.Elements().SingleOrDefault(element => element.Name.LocalName == "name")?.Value;
            if (name is null || name.Length is < 1 or > 64 ||
                name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_') == false || names.Add(name) == false)
            {
                throw new FormatException("Invalid advertised action name.");
            }
        }
        return names;
    }
}
