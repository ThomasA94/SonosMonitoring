using System.Text.Json;
using System.Xml.Linq;

namespace SonosMonitoring.Unit.Tests.Architecture;

/// <summary>Guards the repository's application project dependency boundaries.</summary>
public sealed class ProjectArchitectureTests
{
    private const string CONTRACTS_PROJECT = "SonosMonitoring.Contracts";
    private const string DOMAIN_PROJECT = "SonosMonitoring.Domain";
    private const string PLATFORM_PROJECT = "SonosMonitoring.Platform";
    private const string PROJECTIONS_PROJECT = "SonosMonitoring.Projections";
    private const string TESTING_PACKAGE = "Devbone.Frameworks.Ddd.Testing";

    /// <summary>Prevents the web host from reaching backend persistence or domain code transitively.</summary>
    [Fact]
    public void Web_references_only_contracts_and_observability_directly_and_transitively()
    {
        string root = FindRepositoryRoot();
        string web = GetProjectPath(root, "SonosMonitoring.Web");
        string[] closure = GetProjectReferenceClosure(web);
        Assert.Equal(3, closure.Length);
        Assert.Contains(GetProjectPath(root, CONTRACTS_PROJECT), closure, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(GetProjectPath(root, "SonosMonitoring.Observability"), closure, StringComparer.OrdinalIgnoreCase);
        using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "SonosMonitoring.Web", "obj", "project.assets.json")));
        Assert.DoesNotContain(assets.RootElement.GetProperty("libraries").EnumerateObject(), package =>
            package.Name.StartsWith("Devbone.", StringComparison.OrdinalIgnoreCase) ||
            package.Name.StartsWith("MongoDB.", StringComparison.OrdinalIgnoreCase) ||
            package.Name.StartsWith("Npgsql/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Keeps the deployable example aligned with the authoritative routing names.</summary>
    [Fact]
    public void Projection_example_uses_platform_names()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "infra", "projections.example.json")));
        var aggregate = document.RootElement.GetProperty("EventProjection").GetProperty("Aggregates")[0];
        Assert.Equal(SonosMonitoring.Platform.MonitoringNames.AGGREGATE, aggregate.GetProperty("AggregateName").GetString());
        Assert.Equal(SonosMonitoring.Platform.MonitoringNames.STREAM, aggregate.GetProperty("SourceAddress").GetString());
    }

    /// <summary>Ensures transport contracts and shared platform names stay dependency-free.</summary>
    [Fact]
    public void Contracts_and_platform_projects_have_no_references()
    {
        string repositoryRoot = FindRepositoryRoot();
        string[] projectNames = [CONTRACTS_PROJECT, PLATFORM_PROJECT];

        foreach (string projectName in projectNames)
        {
            string projectPath = GetProjectPath(repositoryRoot, projectName);
            XElement project = XDocument.Load(projectPath).Root
                ?? throw new InvalidOperationException($"Project file '{projectPath}' has no root element.");
            string[] references = project.Descendants()
                .Where(element => element.Name.LocalName is "ProjectReference" or "PackageReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => string.IsNullOrWhiteSpace(include) == false)
                .Select(include => include!)
                .ToArray();

            Assert.Empty(references);
        }
    }

    /// <summary>Ensures projections do not acquire the domain through a project reference chain.</summary>
    [Fact]
    public void Projections_project_graph_does_not_include_the_domain()
    {
        string repositoryRoot = FindRepositoryRoot();
        string projectionsPath = GetProjectPath(repositoryRoot, PROJECTIONS_PROJECT);
        string domainPath = GetProjectPath(repositoryRoot, DOMAIN_PROJECT);
        string[] references = GetProjectReferenceClosure(projectionsPath);

        Assert.False(
            references.Contains(domainPath, StringComparer.OrdinalIgnoreCase),
            $"The project reference graph for '{PROJECTIONS_PROJECT}' includes '{DOMAIN_PROJECT}'.");
    }

    /// <summary>Ensures no production project's resolved dependency graph contains the test harness.</summary>
    [Fact]
    public void Ddd_testing_package_is_absent_from_production_dependency_graphs()
    {
        string repositoryRoot = FindRepositoryRoot();
        string sourceRoot = Path.Combine(repositoryRoot, "src");
        string[] dependencyGraphs = Directory.EnumerateFiles(sourceRoot, "*.deps.json", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(dependencyGraphs);

        foreach (string graphPath in dependencyGraphs)
        {
            using JsonDocument graph = JsonDocument.Parse(File.ReadAllText(graphPath));
            JsonElement libraries = graph.RootElement.GetProperty("libraries");
            string[] packageNames = libraries.EnumerateObject().Select(library => library.Name).ToArray();

            Assert.DoesNotContain(
                packageNames,
                name => name.StartsWith($"{TESTING_PACKAGE}/", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Finds the repository by walking upward from the test assembly directory.</summary>
    /// <returns>The repository root containing the source and test projects.</returns>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", $"{CONTRACTS_PROJECT}", $"{CONTRACTS_PROJECT}.csproj")) &&
                File.Exists(Path.Combine(directory.FullName, "tests", "SonosMonitoring.Unit.Tests", "SonosMonitoring.Unit.Tests.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the SonosMonitoring repository root from the test assembly directory.");
    }

    /// <summary>Resolves a named project file under the repository's source directory.</summary>
    /// <param name="repositoryRoot">The discovered repository root.</param>
    /// <param name="projectName">The application project name.</param>
    /// <returns>The absolute project file path.</returns>
    private static string GetProjectPath(string repositoryRoot, string projectName) =>
        Path.Combine(repositoryRoot, "src", projectName, $"{projectName}.csproj");

    /// <summary>Collects every direct and transitive project reference for one project.</summary>
    /// <param name="projectPath">The root project file to inspect.</param>
    /// <returns>Absolute project paths reachable through project references.</returns>
    private static string[] GetProjectReferenceClosure(string projectPath)
    {
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        Stack<string> pending = new();
        pending.Push(projectPath);

        while (pending.Count > 0)
        {
            string currentPath = Path.GetFullPath(pending.Pop());
            if (visited.Add(currentPath) == false)
            {
                continue;
            }

            XElement project = XDocument.Load(currentPath).Root
                ?? throw new InvalidOperationException($"Project file '{currentPath}' has no root element.");
            string projectDirectory = Path.GetDirectoryName(currentPath)
                ?? throw new InvalidOperationException($"Project file '{currentPath}' has no parent directory.");

            foreach (XElement reference in project.Descendants().Where(element => element.Name.LocalName == "ProjectReference"))
            {
                string? include = reference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                {
                    continue;
                }

                pending.Push(Path.GetFullPath(Path.Combine(projectDirectory, include)));
            }
        }

        return visited.ToArray();
    }
}
