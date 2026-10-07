using Devbone.Frameworks.EventProjection.DependencyInjection;
using Devbone.Frameworks.EventProjection.RabbitMq.DependencyInjection;
using SonosMonitoring.Platform;
using SonosMonitoring.Observability.Diagnostics;

namespace SonosMonitoring.Projections;

/// <summary>Composes the supported RabbitMQ pipeline without a domain dependency or custom event consumer.</summary>
public static class ProjectionHost
{
    /// <summary>Builds the worker; initialization verifies MongoDB's replica-set prerequisite.</summary>
    /// <param name="args">Standard host configuration arguments.</param>
    /// <param name="overrides">Settings of an isolated test deployment.</param>
    public static WebApplication Build(string[] args, IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ApplicationName = typeof(ProjectionHost).Assembly.FullName });

        // Avoid machine-wide log sources that require elevated Windows permissions.
        builder.Logging.ClearProviders().AddJsonConsole();
        if ((builder.Configuration["config"] ?? builder.Configuration["SONOSMONITORING_CONFIG"]) is string configPath)
        {
            builder.Configuration.AddJsonFile(Path.GetFullPath(configPath), optional: false, reloadOnChange: false)
                .AddEnvironmentVariables().AddCommandLine(args);
        }
        if (overrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(overrides);
            if (overrides.ContainsKey("ApplicationLogs:Directory") == false && overrides.ContainsKey("ApplicationLogs:Enabled") == false)
            {
                builder.Configuration["ApplicationLogs:Enabled"] = "false";
            }
        }
        builder.Logging.AddApplicationLogs(builder.Configuration, "projections");
        builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:48083");
        // The platform definition is authoritative for both write routing and source subscriptions.
        builder.Configuration["EventProjection:Aggregates:0:AggregateName"] = MonitoringNames.AGGREGATE;
        builder.Configuration["EventProjection:Aggregates:0:SourceAddress"] = MonitoringNames.STREAM;
        builder.Configuration["EventProjection:Aggregates:1:AggregateName"] = MediaControlNames.AGGREGATE;
        builder.Configuration["EventProjection:Aggregates:1:SourceAddress"] = MediaControlNames.STREAM;
        builder.Services.AddDevboneRabbitMqEventProjection();
        builder.Services.AddDevboneEventProjection(builder.Configuration);
        var app = builder.Build();
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
        return app;
    }
}
