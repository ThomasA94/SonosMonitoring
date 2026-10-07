using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace SonosMonitoring.Observability.Diagnostics;

/// <summary>Registers the shared file-backed diagnostic history without domain or persistence dependencies.</summary>
public static class ApplicationLoggingExtensions
{
    /// <summary>Adds a fixed-source provider and exposes its store for diagnostic queries.</summary>
    /// <param name="builder">Host logging builder.</param>
    /// <param name="configuration">Effective host configuration.</param>
    /// <param name="source">Allowlisted application host name.</param>
    /// <returns>The existing logging builder.</returns>
    public static ILoggingBuilder AddApplicationLogs(this ILoggingBuilder builder, IConfiguration configuration, string source)
    {
        ApplicationLogStore.ValidateSource(source);
        builder.Services.TryAddSingleton(_ => new ApplicationLogStore(configuration));
        builder.Services.AddSingleton<ILoggerProvider>(provider => new ApplicationLogProvider(provider.GetRequiredService<ApplicationLogStore>(), source));
        builder.AddFilter<ApplicationLogProvider>(null, LogLevel.Information);
        return builder;
    }
}
