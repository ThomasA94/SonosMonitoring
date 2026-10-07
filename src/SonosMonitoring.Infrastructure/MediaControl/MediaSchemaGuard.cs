using Microsoft.Extensions.Hosting;
using SonosMonitoring.Application.MediaControl;

namespace SonosMonitoring.Infrastructure.MediaControl;

/// <summary>Refuses startup against an absent or incompatible coordination schema without changing data.</summary>
public sealed class MediaSchemaGuard : IHostedService
{
    private readonly IMediaCoordinationStore _store;

    /// <summary>Receives the same verified store as the execution worker.</summary>
    /// <param name="store">Installation coordination store.</param>
    public MediaSchemaGuard(IMediaCoordinationStore store) => this._store = store;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken) => await this._store.StatusAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
