using SonosMonitoring.Application.MediaControl.Transport;

namespace SonosMonitoring.Integration.Tests.MediaControl;

/// <summary>Provides join preconditions while real persistence handles the operation lifecycle.</summary>
public sealed class FakeGroupDeviceGateway : IMediaDeviceGateway
{
    private int _sendCount;
    /// <summary>Gets the number of dispatched member writes.</summary>
    public int SendCount => Volatile.Read(ref this._sendCount);
    /// <summary>Gets the standalone source context.</summary>
    public MediaTargetContext Source { get; } = new("RINCON_GROUP_B", "household-test", "RINCON_GROUP_B", ["RINCON_GROUP_B"], "source-b");
    /// <summary>Gets the destination expected by the simulated player.</summary>
    public MediaTargetContext Destination { get; } = new("RINCON_GROUP_A", "household-test", "RINCON_GROUP_A", ["RINCON_GROUP_A"], "source-a");

    /// <inheritdoc />
    public Task<MediaPlayback> ReadAsync(string deviceKey, CancellationToken cancellationToken) => Task.FromResult(new MediaPlayback(
        deviceKey == this.Source.DeviceKey ? this.Source : this.Destination, DateTimeOffset.UtcNow,
        "queue", "PLAYING", 20, false, 20, false, null, null, null, null, null, ["join"], null, null));

    /// <inheritdoc />
    public Task<MediaPage> BrowseAsync(string deviceKey, string kind, int offset, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(new MediaPage([], 0, 0, "0"));

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaWriteStep>> PrepareAsync(string deviceKey, string action, MediaTargetContext context,
        MediaWriteInputs inputs, CancellationToken cancellationToken)
    {
        if (action != "join" || deviceKey != this.Source.DeviceKey || inputs.DestinationContext?.SourceToken != this.Destination.SourceToken)
        {
            throw new MediaDeviceException("context_changed");
        }
        return Task.FromResult<IReadOnlyList<MediaWriteStep>>([new(deviceKey, "127.0.0.1", "AVTransport", "SetAVTransportURI",
            new Dictionary<string, string> { ["CurrentURI"] = "x-rincon:" + this.Destination.DeviceKey }, context)
            { DestinationContext = inputs.DestinationContext }]);
    }

    /// <inheritdoc />
    public Task SendAsync(MediaWriteStep step, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref this._sendCount);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool?> ConfirmAsync(string action, MediaTargetContext context, MediaWriteInputs inputs, CancellationToken cancellationToken) =>
        Task.FromResult<bool?>(true);
}
