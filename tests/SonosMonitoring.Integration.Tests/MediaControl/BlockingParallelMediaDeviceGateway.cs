using System.Collections.Concurrent;
using System.Globalization;
using SonosMonitoring.Application.MediaControl.Transport;

namespace SonosMonitoring.Integration.Tests.MediaControl;

/// <summary>Blocks one selected write while recording target-specific effect attempts.</summary>
public sealed class BlockingParallelMediaDeviceGateway : IMediaDeviceGateway
{
    private readonly string _heldDeviceKey;
    private readonly int _heldValue;
    private readonly TaskCompletionSource<bool> _heldSendStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _releaseHeldSend = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<int, int> _sendCountsByValue = new();
    private readonly ConcurrentDictionary<string, int> _volumes = new(StringComparer.Ordinal);

    /// <summary>Creates a simulator that pauses the selected value on the selected target.</summary>
    /// <param name="heldDeviceKey">Target whose request remains in flight.</param>
    /// <param name="heldValue">Unique volume value identifying the held operation.</param>
    public BlockingParallelMediaDeviceGateway(string heldDeviceKey, int heldValue)
    {
        this._heldDeviceKey = heldDeviceKey;
        this._heldValue = heldValue;
    }

    /// <summary>Waits until the held operation reaches its device write.</summary>
    /// <param name="ct">Bounded test cancellation.</param>
    public Task WaitForHeldSendAsync(CancellationToken ct) => this._heldSendStarted.Task.WaitAsync(ct);

    /// <summary>Allows the held device write to complete.</summary>
    public void ReleaseHeldSend() => this._releaseHeldSend.TrySetResult(true);

    /// <summary>Gets how many sends used the specified desired volume.</summary>
    /// <param name="value">Unique operation volume.</param>
    /// <returns>Number of external write attempts.</returns>
    public int SendCountFor(int value) => this._sendCountsByValue.GetValueOrDefault(value);

    /// <inheritdoc />
    public Task<MediaPlayback> ReadAsync(string deviceKey, CancellationToken cancellationToken)
    {
        MediaTargetContext context = CreateContext(deviceKey);
        int volume = this._volumes.GetOrAdd(deviceKey, 20);
        return Task.FromResult(new MediaPlayback(context, DateTimeOffset.UtcNow, "queue", "playing", volume, false,
            volume, false, "Test track", null, null, 120, 10, ["volume"], "normal", false));
    }

    /// <inheritdoc />
    public Task<MediaPage> BrowseAsync(string deviceKey, string kind, int offset, int limit,
        CancellationToken cancellationToken) => Task.FromResult(new MediaPage([], offset, 0, "queue-test"));

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaWriteStep>> PrepareAsync(string deviceKey, string action, MediaTargetContext context,
        MediaWriteInputs inputs, CancellationToken cancellationToken)
    {
        if (action != "volume" || inputs.Value is null or < 0 or > 100 || context.DeviceKey != deviceKey ||
            context.CoordinatorDeviceKey != deviceKey || context.MemberDeviceKeys.Contains(deviceKey) == false)
        {
            throw new MediaDeviceException("target.changed", true);
        }

        MediaWriteStep step = new(deviceKey, "127.0.0.1", "RenderingControl", "SetVolume",
            new Dictionary<string, string>
            {
                ["DesiredVolume"] = inputs.Value.Value.ToString(CultureInfo.InvariantCulture),
            }, context);
        return Task.FromResult<IReadOnlyList<MediaWriteStep>>([step]);
    }

    /// <inheritdoc />
    public async Task SendAsync(MediaWriteStep step, CancellationToken cancellationToken)
    {
        int value = int.Parse(step.Arguments["DesiredVolume"], CultureInfo.InvariantCulture);
        this._sendCountsByValue.AddOrUpdate(value, 1, static (_, count) => count + 1);
        if (step.TargetDeviceKey == this._heldDeviceKey && value == this._heldValue)
        {
            this._heldSendStarted.TrySetResult(true);
            await this._releaseHeldSend.Task.WaitAsync(cancellationToken);
        }

        this._volumes[step.TargetDeviceKey] = value;
    }

    /// <inheritdoc />
    public Task<bool?> ConfirmAsync(string action, MediaTargetContext context, MediaWriteInputs inputs,
        CancellationToken cancellationToken) => Task.FromResult<bool?>(
            this._volumes.TryGetValue(context.DeviceKey, out int volume) && volume == inputs.Value);

    /// <summary>Builds the same stable context expected from a selected standalone player.</summary>
    /// <param name="deviceKey">Player identity.</param>
    /// <returns>Validated test context.</returns>
    private static MediaTargetContext CreateContext(string deviceKey) => new(
        deviceKey,
        "household-test",
        deviceKey,
        [deviceKey],
        "source-test",
        "queue-test");
}
