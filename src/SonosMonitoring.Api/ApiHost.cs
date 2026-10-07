using SonosMonitoring.Api.Collection;
using SonosMonitoring.Api.Alerts;
using SonosMonitoring.Api.History;
using SonosMonitoring.Infrastructure.History;
using SonosMonitoring.Api.Metrics;
using SonosMonitoring.Api.Monitoring;
using SonosMonitoring.Api.Security;
using SonosMonitoring.Infrastructure.Collection;
using SonosMonitoring.Infrastructure.Monitoring;
using SonosMonitoring.Infrastructure.Collection.Configuration;
using SonosMonitoring.Application.Collection;
using SonosMonitoring.Infrastructure.MediaControl;
using SonosMonitoring.Api.MediaControl;
using SonosMonitoring.Api.Diagnostics;
using SonosMonitoring.Observability.Diagnostics;

namespace SonosMonitoring.Api;

/// <summary>Composes the HTTP backend; tests use the same host and override only deployment configuration.</summary>
public static class ApiHost
{
    /// <summary>Builds the application without starting sockets or background services.</summary>
    /// <param name="args">Standard ASP.NET configuration arguments.</param>
    /// <param name="overrides">Explicit isolated test or embedding settings.</param>
    /// <param name="configureServices">Optional embedding composition; tests replace device I/O with an isolated simulator.</param>
    public static WebApplication Build(string[] args, IReadOnlyDictionary<string, string?>? overrides = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ApplicationName = typeof(ApiHost).Assembly.FullName, EnvironmentName = overrides?.GetValueOrDefault("Environment") });

        // Console output works identically in containers and unprivileged local development.
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
        builder.Logging.AddApplicationLogs(builder.Configuration, "api");
        builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:48080");
        builder.Services.AddProblemDetails();
        if (builder.Configuration["Metrics:PrometheusUrl"] is string prometheusUrl)
        {
            if (Uri.TryCreate(prometheusUrl, UriKind.Absolute, out var address) == false ||
                (address.Scheme != "http" && address.Scheme != "https") ||
                address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0 ||
                address.AbsolutePath.EndsWith('/') == false)
            {
                throw new InvalidOperationException("Metrics:PrometheusUrl must be an HTTP base URL ending with '/', without credentials, query, or fragment.");
            }
            builder.Services.AddHttpClient<SonosMonitoring.Application.Metrics.IMetricHistory, SonosMonitoring.Infrastructure.Metrics.PrometheusHistory>(client =>
            {
                client.BaseAddress = address;
                client.Timeout = TimeSpan.FromSeconds(10);
            }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false });
            builder.Services.AddHttpClient<SonosMonitoring.Application.Alerts.IAlertMessagesReader, SonosMonitoring.Infrastructure.Alerts.PrometheusAlertMessagesReader>(client =>
            {
                client.BaseAddress = address;
                client.Timeout = TimeSpan.FromSeconds(10);
            }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false });
        }
        CollectorOptions collectorDefaults = new();
        builder.Configuration.GetSection("Collector").Bind(collectorDefaults);
        if (collectorDefaults.AllowLoopback && builder.Environment.IsEnvironment("Testing") == false)
        {
            throw new InvalidOperationException("Loopback player targets are permitted only in the Testing environment.");
        }
        builder.Services.AddSingleton(new CollectorConfigurationStore(builder.Configuration["CollectionSettings:Path"] ??
            Path.Combine(builder.Environment.ContentRootPath, ".local", "collector-settings.json")));
        builder.Services.AddSingleton(provider => new CollectorRuntime(provider, provider.GetRequiredService<CollectorConfigurationStore>(), collectorDefaults));
        builder.Services.AddSingleton<ICollectorSnapshotReader>(provider => provider.GetRequiredService<CollectorRuntime>());
        builder.Services.AddSingleton(provider => provider.GetRequiredService<CollectorRuntime>().State);
        builder.Services.AddHostedService(provider => provider.GetRequiredService<CollectorRuntime>());
        if (builder.Configuration.GetValue<bool>("Backend:Enabled"))
        {
            builder.Services.AddMonitoringBackend(builder.Configuration);
        }
        builder.Services.AddObservationHistory(builder.Configuration);
        builder.Services.AddMediaControl(builder.Configuration);
        configureServices?.Invoke(builder.Services);
        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseMiddleware<LocalAccessMiddleware>();
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
        app.MapGet("/health/ready", async (HttpContext context) =>
{
    var readiness = context.RequestServices.GetService<BackendReadiness>();
    var ready = readiness is null || await readiness.CheckAsync(context.RequestAborted);
    return Results.Json(new { status = ready ? "ready" : "unavailable" }, statusCode: ready ? 200 : 503);
});
        app.MapMonitoring();
        app.MapCollection();
        app.MapCollectorConfiguration();
        app.MapCollectionExport();
        app.MapMetricHistory();
        app.MapAlerts();
        app.MapObservationHistory();
        app.MapMediaControl();
        app.MapApplicationLogs();
        return app;
    }
}
