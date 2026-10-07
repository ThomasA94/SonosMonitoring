using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SonosMonitoring.Web.Localization;

namespace SonosMonitoring.Web.Tests.Localization;

/// <summary>Guards full bilingual coverage and culture-sensitive measurement formatting.</summary>
public sealed class ResourceTests
{
    /// <summary>Both resources have identical keys and every literal UI lookup is present.</summary>
    [Fact]
    public void Resources_cover_all_static_lookups_in_both_languages()
    {
        string root = FindRoot();
        string directory = Path.Combine(root, "src", "SonosMonitoring.Web");
        var german = Read(Path.Combine(directory, "Localization", "Strings.resx"));
        var english = Read(Path.Combine(directory, "Localization", "Strings.en.resx"));
        Assert.Equal(german.Keys.Order(), english.Keys.Order());
        Assert.Equal(german.Count, german.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(english.Count, english.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(german.Values.Concat(english.Values), value => Assert.False(string.IsNullOrWhiteSpace(value)));
        foreach (string path in Directory.EnumerateFiles(directory, "*.razor", SearchOption.AllDirectories))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(path), "Text\\[\"([^\"]+)\"\\]"))
            {
                Assert.True(german.ContainsKey(match.Groups[1].Value), $"Missing {match.Groups[1].Value} in {path}");
            }
        }
    }

    /// <summary>Numbers and labels follow the circuit culture rather than a hard-coded locale.</summary>
    [Fact]
    public void Culture_changes_labels_and_numeric_separators()
    {
        var original = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            var text = new UiText(new BrowserTimeZone());
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("de");
            Assert.Equal("Geräte", text["Devices"]);
            Assert.Equal("1.234,50", text.Number(1234.5));
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");
            Assert.Equal("Devices", text["Devices"]);
            Assert.Equal("1,234.50", text.Number(1234.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    /// <summary>Reads each resource while rejecting accidental duplicate keys.</summary>
    /// <param name="path">Resource file.</param>
    private static Dictionary<string, string> Read(string path) => XDocument.Load(path).Root!.Elements("data")
        .ToDictionary(element => element.Attribute("name")!.Value, element => element.Element("value")!.Value);

    /// <summary>Locates the checkout from the test output directory.</summary>
    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && File.Exists(Path.Combine(directory.FullName, "SonosMonitoring.slnx")) == false)
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
