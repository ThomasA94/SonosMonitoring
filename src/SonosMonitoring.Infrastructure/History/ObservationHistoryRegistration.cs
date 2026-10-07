using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SonosMonitoring.Application.History;

namespace SonosMonitoring.Infrastructure.History;

/// <summary>Registers the explicitly enabled observation journal independently of event-store composition.</summary>
public static class ObservationHistoryRegistration
{
    /// <summary>Validates bounded configuration and registers a schema guard, worker, and query port.</summary>
    /// <param name="services">API composition root.</param>
    /// <param name="configuration">External journal configuration.</param>
    public static IServiceCollection AddObservationHistory(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("History").Get<ObservationHistoryOptions>() ?? new();
        if (options.Enabled == false)
        { return services; }
        if (string.IsNullOrWhiteSpace(options.ConnectionString) || options.RetentionDays is < 1 or > 400 ||
            options.MaxEntries is < 100 or > 100000 || options.SampleInterval < TimeSpan.FromSeconds(1) ||
            options.SampleInterval > TimeSpan.FromMinutes(1))
        {
            throw new InvalidOperationException("History requires a PostgreSQL connection string, retention 1..400 days, 100..100000 entries, and a sampling interval 1..60 seconds.");
        }
        if (configuration.GetValue("Collector:MaxKnownDevices", 256) > 256)
        {
            throw new InvalidOperationException("History supports at most 256 known collector devices.");
        }
        services.AddSingleton(options);
        services.AddSingleton<PostgresObservationHistory>();
        services.AddSingleton<IObservationHistory>(provider => provider.GetRequiredService<PostgresObservationHistory>());
        services.AddHostedService<ObservationHistoryWorker>();
        return services;
    }
}
