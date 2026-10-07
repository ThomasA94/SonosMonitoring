using Microsoft.Extensions.Logging;

namespace SonosMonitoring.Observability.Diagnostics;

/// <summary>Copies useful application events into the bounded local diagnostic history.</summary>
[ProviderAlias("ApplicationLogs")]
public sealed class ApplicationLogProvider : ILoggerProvider
{
    private readonly ApplicationLogStore _store;
    private readonly string _source;

    /// <summary>Creates one provider for a fixed application host.</summary>
    /// <param name="store">Shared diagnostic store.</param>
    /// <param name="source">Allowlisted host: api, web, or projections.</param>
    public ApplicationLogProvider(ApplicationLogStore store, string source)
    {
        ApplicationLogStore.ValidateSource(source);
        this._store = store;
        this._source = source;
    }

    /// <summary>Creates a category logger sharing this host's storage.</summary>
    /// <param name="categoryName">Logger category.</param>
    /// <returns>Logger that sanitizes messages before persistence.</returns>
    public ILogger CreateLogger(string categoryName) => new ApplicationLogger(this._store, this._source, categoryName);

    /// <summary>Releases no resources because every append closes its own file.</summary>
    public void Dispose()
    {
    }
}
