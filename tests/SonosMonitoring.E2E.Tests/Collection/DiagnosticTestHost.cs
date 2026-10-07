using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SonosMonitoring.Api.Collection;
using SonosMonitoring.Application.Collection;
using SonosMonitoring.Contracts.Collection;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.E2E.Tests.Collection;

/// <summary>Owns one actual HTTP API host and its loopback client for a journey case.</summary>
internal sealed class DiagnosticTestHost : IAsyncDisposable
{
    /// <summary>Gets the running host.</summary>
    public WebApplication App { get; }

    /// <summary>Gets the client addressed to the host's ephemeral listener.</summary>
    public HttpClient Client { get; }

    /// <summary>Creates a fully isolated collector generation and maps its live and export endpoints.</summary>
    /// <param name="app">The started host.</param>
    private DiagnosticTestHost(WebApplication app)
    {
        this.App = app;
        this.Client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }

    /// <summary>Builds and starts the real loopback collector and API routes.</summary>
    /// <param name="policy">The source collection policy.</param>
    /// <returns>The started HTTP fixture.</returns>
    public static async Task<DiagnosticTestHost> StartAsync(IDeviceCollectionPolicy policy)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(policy);
        builder.Services.AddSonosCollector(options =>
        {
            options.Enabled = true;
            options.AllowLoopback = true;
            options.AllowedCidrs = ["127.0.0.2/32", "127.0.0.3/32"];
            options.PollInterval = TimeSpan.FromSeconds(1);
            options.DiscoveryInterval = TimeSpan.FromSeconds(5);
            options.StaleAfter = TimeSpan.FromSeconds(2);
            options.RequestTimeout = TimeSpan.FromSeconds(1);
            options.SweepTimeout = TimeSpan.FromSeconds(5);
            options.MaxResponseBytes = 4096;
        });

        WebApplication app = builder.Build();
        app.MapCollection();
        app.MapCollectionExport();
        await app.StartAsync();
        return new DiagnosticTestHost(app);
    }

    /// <summary>Disposes the client and stops the host.</summary>
    public async ValueTask DisposeAsync()
    {
        this.Client.Dispose();
        await this.App.DisposeAsync();
    }
}
