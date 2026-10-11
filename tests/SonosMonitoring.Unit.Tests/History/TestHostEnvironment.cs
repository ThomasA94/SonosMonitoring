using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace SonosMonitoring.Unit.Tests.History;

/// <summary>Supplies the minimal host identity needed to construct an inactive collector generation.</summary>
internal sealed class TestHostEnvironment : IHostEnvironment
{
    /// <summary>Creates an isolated content root for configuration persistence.</summary>
    /// <param name="contentRootPath">Temporary test directory.</param>
    public TestHostEnvironment(string contentRootPath)
    {
        this.ContentRootPath = contentRootPath;
        this.ContentRootFileProvider = new NullFileProvider();
    }

    /// <inheritdoc />
    public string EnvironmentName { get; set; } = Environments.Development;

    /// <inheritdoc />
    public string ApplicationName { get; set; } = "SonosMonitoring.Unit.Tests";

    /// <inheritdoc />
    public string ContentRootPath { get; set; }

    /// <inheritdoc />
    public IFileProvider ContentRootFileProvider { get; set; }
}
