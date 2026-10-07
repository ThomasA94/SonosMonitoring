using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SonosMonitoring.Application.Collection;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Registers the read-only Sonos collector and its snapshot query port.</summary>
public static class CollectorRegistration
{
    /// <summary>Adds collection, disabled by default until explicit target ranges are configured.</summary>
    /// <param name="services">Host service collection.</param>
    /// <param name="configure">Optional explicit collection settings.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddSonosCollector(this IServiceCollection services, Action<CollectorOptions>? configure = null)
    {
        CollectorOptions options = new();
        configure?.Invoke(options);
        return services.AddSonosCollector(options);
    }

    /// <summary>Registers one immutable generation from an already captured configuration.</summary>
    /// <param name="services">Generation-owned service collection.</param>
    /// <param name="options">Captured options, validated before service construction.</param>
    internal static IServiceCollection AddSonosCollector(this IServiceCollection services, CollectorOptions options)
    {
        Ipv4TargetPolicy policy = CollectorConfigurationValidator.Validate(options);

        services.AddSingleton(options);
        services.AddSingleton(provider =>
        {
            IHostEnvironment environment = provider.GetRequiredService<IHostEnvironment>();
            if (options.AllowLoopback && environment.IsEnvironment("Testing") == false)
            {
                throw new InvalidOperationException("Loopback collection is only allowed in the Testing environment.");
            }

            return policy;
        });
        services.AddSingleton<PlayerProbe>();
        services.AddSingleton<CollectorState>();
        services.AddSingleton<Diagnostics.DeviceDiagnosticsCollector>();
        services.AddSingleton<TopologyCollector>();
        services.AddSingleton<TopologyDiscovery>();
        services.AddSingleton<SsdpSearch>();
        services.AddSingleton<SsdpDiscovery>();
        services.AddSingleton<ICollectorSnapshotReader>(provider => provider.GetRequiredService<CollectorState>());
        services.AddHostedService<CollectorWorker>();
        services.AddSingleton<Gena.GenaTransport>();
        services.AddHostedService<Gena.GenaWorker>();
        return services;
    }
}
