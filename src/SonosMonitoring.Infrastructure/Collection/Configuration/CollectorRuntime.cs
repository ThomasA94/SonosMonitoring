using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SonosMonitoring.Application.Collection;

namespace SonosMonitoring.Infrastructure.Collection.Configuration;

/// <summary>Replaces a complete collector generation after stopping its requests and callback listener.</summary>
public sealed class CollectorRuntime : IHostedService, ICollectorSnapshotReader, IAsyncDisposable
{
    private readonly IServiceProvider _host;
    private readonly CollectorConfigurationStore _store;
    private readonly SemaphoreSlim _gate = new(1);
    private CollectorConfigurationDocument _document;
    private ServiceProvider _generation;
    private CollectorState _state;
    private bool _disposed;

    /// <summary>Loads and validates persistent settings without starting network traffic.</summary>
    /// <param name="host">Host services supplying environment, logs and optional monitoring policy.</param>
    /// <param name="store">Technical settings persistence.</param>
    /// <param name="defaults">Deployment defaults used before the first web save.</param>
    public CollectorRuntime(IServiceProvider host, CollectorConfigurationStore store, CollectorOptions defaults)
    {
        this._host = host;
        this._store = store;
        this._document = store.Load(defaults);
        this._generation = this.CreateGeneration(this._document.Options);
        this._state = this._generation.GetRequiredService<CollectorState>();
    }

    /// <summary>Gets the current state for diagnostics and isolated test injection.</summary>
    public CollectorState State => Volatile.Read(ref this._state);

    /// <summary>Returns a detached settings document so callers cannot mutate active workers.</summary>
    public CollectorConfigurationDocument GetConfiguration() =>
        JsonSerializer.Deserialize<CollectorConfigurationDocument>(JsonSerializer.Serialize(Volatile.Read(ref this._document)))!;

    /// <inheritdoc />
    public CollectorSnapshot GetSnapshot() => this.State.GetSnapshot();

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var worker in this._generation.GetServices<IHostedService>())
        {
            await worker.StartAsync(cancellationToken);
        }
    }

    /// <summary>Validates and commits one revision; a repeated identical save has no side effects.</summary>
    /// <param name="options">Complete candidate settings.</param>
    /// <param name="expectedRevision">Revision originally shown in the editor.</param>
    /// <param name="requestId">Stable save identity, retained on ambiguous transport failures.</param>
    /// <returns>False for a concurrency conflict or reused identity with changed content.</returns>
    public async Task<bool> ApplyAsync(CollectorOptions options, Guid expectedRevision, Guid requestId)
    {
        await this._gate.WaitAsync();
        try
        {
            options = JsonSerializer.Deserialize<CollectorOptions>(JsonSerializer.Serialize(options))!;
            options.AllowLoopback = this._document.Options.AllowLoopback;
            CollectorConfigurationValidator.Validate(options);
            if (requestId == Guid.Empty)
            {
                throw new ArgumentException("A nonempty save identity is required.", nameof(requestId));
            }
            if (requestId == this._document.Revision)
            {
                return JsonSerializer.Serialize(options) == JsonSerializer.Serialize(this._document.Options);
            }
            if (expectedRevision != this._document.Revision)
            {
                return false;
            }

            ServiceProvider next = this.CreateGeneration(options);
            var document = new CollectorConfigurationDocument(requestId, options);
            try
            {
                this._store.Save(document);
            }
            catch
            {
                await next.DisposeAsync();
                throw;
            }
            // After the durable commit, finish activation even if the browser disconnects.
            await this.StopGenerationAsync();
            await this._generation.DisposeAsync();
            this._generation = next;
            Volatile.Write(ref this._state, next.GetRequiredService<CollectorState>());
            Volatile.Write(ref this._document, document);
            await this.StartAsync(CancellationToken.None);
            return true;
        }
        finally
        {
            this._gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await this._gate.WaitAsync(cancellationToken);
        try
        {
            await this.StopGenerationAsync();
        }
        finally
        {
            this._gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
        {
            return;
        }
        this._disposed = true;
        await this._generation.DisposeAsync();
        this._gate.Dispose();
    }

    /// <summary>Waits for cancellation and bounded GENA cleanup before any replacement starts.</summary>
    private async Task StopGenerationAsync()
    {
        foreach (var worker in this._generation.GetServices<IHostedService>().Reverse())
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Builds only collector-owned services, retaining the host's application policy port.</summary>
    /// <param name="options">Validated immutable settings for this generation.</param>
    private ServiceProvider CreateGeneration(CollectorOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton(this._host.GetRequiredService<IHostEnvironment>());
        services.AddSingleton(this._host.GetRequiredService<ILoggerFactory>());
        services.AddLogging();
        var policy = this._host.GetService<IDeviceCollectionPolicy>();
        if (policy is not null)
        {
            services.AddSingleton(policy);
        }
        services.AddSonosCollector(options);
        return services.BuildServiceProvider();
    }
}
