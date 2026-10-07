using Devbone.Frameworks.EventSourcing.Repositories.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SonosMonitoring.Application.MediaControl;
using SonosMonitoring.Application.MediaControl.Transport;
using SonosMonitoring.Infrastructure.MediaControl.Transport;

namespace SonosMonitoring.Infrastructure.MediaControl;

/// <summary>Composes the optional media slice from existing framework persistence and narrow device ports.</summary>
public static class MediaControlRegistration
{
    /// <summary>Validates deployment limits and registers the executor only when explicitly configured.</summary>
    /// <param name="services">API services.</param>
    /// <param name="configuration">External deployment configuration.</param>
    /// <returns>The original service collection.</returns>
    public static IServiceCollection AddMediaControl(this IServiceCollection services, IConfiguration configuration)
    {
        MediaControlOptions options = configuration.GetSection("MediaControl").Get<MediaControlOptions>() ?? new();
        if (options.Enabled == false)
        {
            return services;
        }
        if (configuration.GetValue<bool>("Backend:Enabled") == false || options.MaxPending is < 1 or > 1024 ||
            options.LifetimeSeconds is < 5 or > 300 || options.ExecutionSeconds is < 5 or > 120)
        {
            throw new InvalidOperationException("Media control requires the backend and bounded queue, lifetime and execution settings.");
        }
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            var settings = configuration.GetSection("EventStore").Get<EventStoreSettings>()!;
            options.ConnectionString = new NpgsqlConnectionStringBuilder
            {
                Host = settings.Host,
                Port = checked((int)settings.Port),
                Database = settings.DatabaseName,
                Username = settings.User,
                Password = settings.Password,
                Timeout = 5,
                CommandTimeout = 5,
            }.ConnectionString;
        }
        services.AddSingleton(options);
        services.AddSingleton<IMediaCoordinationStore, PostgresMediaCoordinationStore>();
        services.AddSingleton<IMediaDeviceGateway, MediaDeviceGateway>();
        services.AddSingleton<IMediaOperationProjection, MongoMediaOperationProjection>();
        services.AddScoped<MediaOperationExecutor>();
        services.AddScoped<MediaOperationReader>();
        services.AddScoped(provider => new MediaRequestService(provider.GetRequiredService<MediaOperationExecutor>(),
            provider.GetRequiredService<MediaOperationReader>(), provider.GetRequiredService<IMediaCoordinationStore>(),
            provider.GetRequiredService<IMediaDeviceGateway>(), options.LifetimeSeconds));
        services.AddScoped<MediaExecutionService>();
        services.AddHostedService<MediaSchemaGuard>();
        services.AddHostedService<MediaControlWorker>();
        return services;
    }
}
