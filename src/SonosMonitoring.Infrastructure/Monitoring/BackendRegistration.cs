using Devbone.Frameworks.EventBus.Bus;
using Devbone.Frameworks.EventBus.DependencyInjection;
using Devbone.Frameworks.EventBus.Outbox.DependencyInjection;
using Devbone.Frameworks.EventBus.Outbox.Dispatching;
using Devbone.Frameworks.EventBus.RabbitMq.DependencyInjection;
using Devbone.Frameworks.EventSourcing.Repositories.Settings;
using Devbone.Frameworks.EventSourcing.Ddd.DependencyInjection;
using Devbone.Frameworks.EventSourcing.Ddd.Mapping;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using SonosMonitoring.Application.Monitoring;
using SonosMonitoring.Platform;

namespace SonosMonitoring.Infrastructure.Monitoring;

/// <summary>Composes only the supported Direct adapter, durable outbox and selected RabbitMQ transport.</summary>
public static class BackendRegistration
{
    /// <summary>Registers persistence and query ports; the schema must already be initialized.</summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">Externally supplied database and broker configuration.</param>
    public static IServiceCollection AddMonitoringBackend(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("EventStore").Get<EventStoreSettings>()
            ?? throw new InvalidOperationException("EventStore configuration is required.");
        services.AddDevboneEventSourcing(settings, MonitoringRouting.Create(), new EventPublicationContext { SourceId = "sonosmonitoring-api" });
        services.AddDevboneRabbitMqEventBus();
        var rabbit = configuration.GetSection("Backend:RabbitMq").Get<RabbitMqBusOptions>()
            ?? throw new InvalidOperationException("Backend:RabbitMq configuration is required.");
        services.AddDevboneEventBus(new EventBusTargetsOptions
        {
            Targets = { [MonitoringNames.BUS] = new EventBusTargetOptions { Transport = "rabbitmq", RabbitMq = rabbit } },
        });
        services.AddDevboneOutboxDispatcher(new OutboxDispatcherOptions
        {
            EventStoreId = MonitoringNames.STORE,
            Mode = configuration.GetValue("Backend:DispatchOutbox", true) ? OutboxDispatchMode.Embedded : OutboxDispatchMode.Disabled,
            RecoveryInterval = TimeSpan.FromSeconds(2),
        });
        var mongo = configuration["Backend:MongoConnectionString"] ?? throw new InvalidOperationException("Backend:MongoConnectionString is required.");
        var database = configuration["Backend:MongoDatabase"] ?? throw new InvalidOperationException("Backend:MongoDatabase is required.");
        services.AddSingleton<IMongoClient>(new MongoClient(mongo));
        services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(database));
        services.AddSingleton<IMonitoringQuery, MongoMonitoringQuery>();
        services.AddScoped<MonitoringExecutor>();
        services.AddScoped<MonitoringTokenReader>();
        services.AddSingleton<SonosMonitoring.Application.Collection.IDeviceCollectionPolicy, ProjectedCollectionPolicy>();
        services.AddHostedService<EventStoreSchemaGuard>();
        services.AddSingleton<BackendReadiness>();
        return services;
    }
}
