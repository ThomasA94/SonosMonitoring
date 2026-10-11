using SonosMonitoring.Application.MediaControl.Transport;

namespace SonosMonitoring.Unit.Tests.MediaControl;

/// <summary>Provides two volume steps and rejects a selected send before any device effect.</summary>
internal sealed class TestMediaDeviceGateway(int rejectOnAttempt) : IMediaDeviceGateway
{
    private int _sendCount;

    /// <summary>Gets the number of send attempts.</summary>
    public int SendCount => this._sendCount;

    /// <inheritdoc />
    public Task<MediaPlayback> ReadAsync(string deviceKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task<MediaPage> BrowseAsync(string deviceKey, string kind, int offset, int limit,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaWriteStep>> PrepareAsync(string deviceKey, string action,
        MediaTargetContext context, MediaWriteInputs inputs, CancellationToken cancellationToken)
    {
        IReadOnlyList<MediaWriteStep> steps = [
            new(context.DeviceKey, "192.0.2.10", "RenderingControl", "SetVolume", new Dictionary<string, string>(), context),
            new(context.DeviceKey, "192.0.2.10", "RenderingControl", "SetVolume", new Dictionary<string, string>(), context)];
        return Task.FromResult(steps);
    }

    /// <inheritdoc />
    public Task SendAsync(MediaWriteStep step, CancellationToken cancellationToken)
    {
        this._sendCount++;
        return this._sendCount == rejectOnAttempt
            ? throw new MediaDeviceException("action_rejected", definiteRejection: true)
            : Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool?> ConfirmAsync(string action, MediaTargetContext context, MediaWriteInputs inputs,
        CancellationToken cancellationToken) => Task.FromResult<bool?>(true);
}
