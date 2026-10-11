using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SonosMonitoring.Unit.Tests.Architecture;

/// <summary>Checks that the checked-in Devbone API guide matches the packages this repository uses.</summary>
public sealed class FrameworkApiGuideVersionTests
{
    private const string PACKAGE_PREFIX = "Devbone.Frameworks.";

    /// <summary>Keeps used Devbone package versions aligned across the guide, lockfiles, and central package props.</summary>
    [Fact]
    public void Guide_versions_match_used_devbone_package_versions()
    {
        string repositoryRoot = FindRepositoryRoot();
        Dictionary<string, string> guideVersions = ReadGuideVersions(repositoryRoot);
        Dictionary<string, string> usedVersions = ReadLockedVersions(repositoryRoot);

        Assert.NotEmpty(usedVersions);
        foreach ((string package, string version) in usedVersions)
        {
            Assert.True(
                guideVersions.TryGetValue(package, out string? guideVersion),
                $"The framework guide does not list used package '{package}'.");
            Assert.Equal(version, guideVersion);
        }

        XElement packageProps = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Packages.props"))
            .Root ?? throw new InvalidOperationException("Directory.Packages.props has no root element.");
        foreach (XElement packageVersion in packageProps.Descendants().Where(element => element.Name.LocalName == "PackageVersion"))
        {
            string? package = packageVersion.Attribute("Include")?.Value;
            if (package?.StartsWith(PACKAGE_PREFIX, StringComparison.Ordinal) != true)
            {
                continue;
            }

            string version = packageVersion.Attribute("Version")?.Value
                ?? throw new InvalidOperationException($"Central package version for '{package}' is missing.");
            Assert.True(
                guideVersions.TryGetValue(package, out string? guideVersion),
                $"The framework guide does not list centrally managed package '{package}'.");
            Assert.Equal(version, guideVersion);
        }
    }

    /// <summary>Reads the generated package matrix between its source guide markers.</summary>
    /// <param name="repositoryRoot">The discovered repository root.</param>
    /// <returns>Package names and versions published in the guide table.</returns>
    private static Dictionary<string, string> ReadGuideVersions(string repositoryRoot)
    {
        string guide = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "framework", "FRAMEWORK_API_GUIDE.md"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Match section = Regex.Match(
            guide,
            "<!-- devbone:versions:begin[^>]*-->(.*?)<!-- devbone:versions:end -->",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(section.Success, "The framework guide's generated version table markers are missing.");

        Dictionary<string, string> versions = new(StringComparer.Ordinal);
        foreach (Match row in Regex.Matches(
            section.Groups[1].Value,
            "^\\| `(?<package>Devbone\\.Frameworks\\.[^`]+)` \\| (?<version>[^|]+) \\|$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant))
        {
            string package = row.Groups["package"].Value;
            string version = row.Groups["version"].Value.Trim();
            Assert.True(versions.TryAdd(package, version), $"The guide lists package '{package}' more than once.");
        }

        Assert.NotEmpty(versions);
        return versions;
    }

    /// <summary>Collects resolved Devbone versions from every application and test lockfile.</summary>
    /// <param name="repositoryRoot">The discovered repository root.</param>
    /// <returns>Unique package names and their consistent resolved versions.</returns>
    private static Dictionary<string, string> ReadLockedVersions(string repositoryRoot)
    {
        Dictionary<string, string> versions = new(StringComparer.Ordinal);
        foreach (string area in new[] { "src", "tests" })
        {
            string areaPath = Path.Combine(repositoryRoot, area);
            foreach (string lockPath in Directory.EnumerateFiles(areaPath, "packages.lock.json", SearchOption.AllDirectories))
            {
                using JsonDocument lockFile = JsonDocument.Parse(File.ReadAllText(lockPath));
                JsonElement targets = lockFile.RootElement.GetProperty("dependencies");
                foreach (JsonProperty target in targets.EnumerateObject())
                {
                    foreach (JsonProperty dependency in target.Value.EnumerateObject())
                    {
                        if (dependency.Name.StartsWith(PACKAGE_PREFIX, StringComparison.Ordinal) == false)
                        {
                            continue;
                        }

                        string version = dependency.Value.GetProperty("resolved").GetString()
                            ?? throw new InvalidOperationException($"Resolved version is missing for '{dependency.Name}' in '{lockPath}'.");
                        if (versions.TryGetValue(dependency.Name, out string? existingVersion))
                        {
                            Assert.Equal(existingVersion, version);
                        }
                        else
                        {
                            versions.Add(dependency.Name, version);
                        }
                    }
                }
            }
        }

        return versions;
    }

    /// <summary>Finds the repository containing the application and this test project.</summary>
    /// <returns>The absolute repository root.</returns>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "SonosMonitoring.Contracts", "SonosMonitoring.Contracts.csproj")) &&
                File.Exists(Path.Combine(directory.FullName, "tests", "SonosMonitoring.Unit.Tests", "SonosMonitoring.Unit.Tests.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the SonosMonitoring repository root from the test assembly directory.");
    }
}
