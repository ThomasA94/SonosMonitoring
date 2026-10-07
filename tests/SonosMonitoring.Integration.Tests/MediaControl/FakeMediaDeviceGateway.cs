using SonosMonitoring.Application.MediaControl.Transport;

namespace SonosMonitoring.Integration.Tests.MediaControl;

/// <summary>Simulates bounded local reads and records precisely how many external writes would occur.</summary>
public sealed class FakeMediaDeviceGateway : IMediaDeviceGateway
{
    private readonly TaskCompletionSource<bool> _executionPreparationReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _continueExecutionPreparation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _firstSendReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _continueFirstSend = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _preparationCount;
    private int _sendCount;
    private int _volume = 20;
    private int? _delayedVolume;

    /// <summary>Gets the selected stable test player identity.</summary>
    public string DeviceKey { get; } = "RINCON_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
    /// <summary>Gets the number of device write attempts.</summary>
    public int SendCount => Volatile.Read(ref this._sendCount);
    /// <summary>Gets how often the fake validated an action before and during execution.</summary>
    public int PreparationCount => Volatile.Read(ref this._preparationCount);
    /// <summary>Gets the simulated current player volume.</summary>
    public int CurrentVolume => Volatile.Read(ref this._volume);
    /// <summary>Gets whether the next execution preparation waits for the test to continue.</summary>
    public bool HoldExecutionPreparation { get; set; }
    /// <summary>Gets whether a sent write reports an ambiguous transport failure.</summary>
    public bool ThrowAfterSend { get; set; }
    /// <summary>Gets or sets the write-attempt number whose response is lost.</summary>
    public int? ThrowOnSendAttempt { get; set; }
    /// <summary>Gets or sets the write-attempt number definitively refused before any device effect.</summary>
    public int? RejectOnSendAttempt { get; set; }
    /// <summary>Gets or sets whether a sent write takes effect only when the test explicitly releases it.</summary>
    public bool DelayEffect { get; set; }
    /// <summary>Gets or sets the number of distinct writing steps in one prepared action.</summary>
    public int StepsPerOperation { get; set; } = 1;
    /// <summary>Gets or sets whether the first write acknowledgement waits for an operator decision.</summary>
    public bool HoldFirstSendCompletion { get; set; }
    /// <summary>Gets or sets whether execution-time preparation definitely rejects the action.</summary>
    public bool RejectExecutionPreparation { get; set; }

    /// <summary>Waits until the first device write has occurred but its acknowledgement is held.</summary>
    /// <param name="ct">Bounded test cancellation.</param>
    public Task WaitForFirstSendAsync(CancellationToken ct) => this._firstSendReached.Task.WaitAsync(ct);

    /// <summary>Allows the first simulated device acknowledgement to finish.</summary>
    public void ContinueFirstSend() => this._continueFirstSend.TrySetResult(true);

    /// <summary>Applies an old delayed write after a newer action to expose the remaining device risk.</summary>
    public void ApplyDelayedEffect()
    {
        if (this._delayedVolume is int value)
        {
            Interlocked.Exchange(ref this._volume, value);
            this._delayedVolume = null;
        }
    }

    /// <summary>Signals that a worker reached its second, execution-time preparation.</summary>
    /// <param name="ct">Bounded test cancellation.</param>
    public Task WaitForExecutionPreparationAsync(CancellationToken ct) =>
        this._executionPreparationReached.Task.WaitAsync(ct);

    /// <summary>Allows a waiting worker to continue after a permission decision.</summary>
    public void ContinueExecutionPreparation() => this._continueExecutionPreparation.TrySetResult(true);

    /// <inheritdoc />
    public Task<MediaPlayback> ReadAsync(string deviceKey, CancellationToken cancellationToken)
    {
        MediaTargetContext context = new(this.DeviceKey, "household-test", this.DeviceKey,
            [this.DeviceKey], "source-test", "queue-test");
        MediaPlayback playback = new(context, DateTimeOffset.UtcNow, "queue", "playing", this.CurrentVolume, false,
            this.CurrentVolume, false, "Test track", null, null, 120, 10, ["volume", "pause"], "normal", false);
        return Task.FromResult(playback);
    }

    /// <inheritdoc />
    public Task<MediaPage> BrowseAsync(string deviceKey, string kind, int offset, int limit,
        CancellationToken cancellationToken) => Task.FromResult(new MediaPage([], offset, 0, "queue-test"));

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaWriteStep>> PrepareAsync(string deviceKey, string action,
        MediaTargetContext context, MediaWriteInputs inputs, CancellationToken cancellationToken)
    {
        if (deviceKey != this.DeviceKey || action != "volume" || inputs.Value is null or < 0 or > 100 ||
            context.DeviceKey != this.DeviceKey || context.CoordinatorDeviceKey != this.DeviceKey)
        {
            throw new MediaDeviceException("target.changed", true);
        }
        int count = Interlocked.Increment(ref this._preparationCount);
        if (count > 1 && this.RejectExecutionPreparation)
        {
            throw new MediaDeviceException("action_rejected", true);
        }
        if (count > 1 && this.HoldExecutionPreparation)
        {
            this._executionPreparationReached.TrySetResult(true);
            await this._continueExecutionPreparation.Task.WaitAsync(cancellationToken);
        }
        return Enumerable.Range(0, this.StepsPerOperation).Select(index =>
            new MediaWriteStep(this.DeviceKey, "127.0.0.1", "RenderingControl", "SetVolume",
                new Dictionary<string, string>
                {
                    ["DesiredVolume"] = (index + 1 == this.StepsPerOperation
                        ? inputs.Value : Math.Max(0, inputs.Value!.Value - 1)).ToString()!,
                }, context)).ToArray();
    }

    /// <inheritdoc />
    public async Task SendAsync(MediaWriteStep step, CancellationToken cancellationToken)
    {
        int attempt = Interlocked.Increment(ref this._sendCount);
        if (this.RejectOnSendAttempt == attempt)
        {
            throw new MediaDeviceException("action_rejected", true);
        }
        int value = int.Parse(step.Arguments["DesiredVolume"], System.Globalization.CultureInfo.InvariantCulture);
        if (this.DelayEffect)
        {
            this._delayedVolume = value;
        }
        else
        {
            Interlocked.Exchange(ref this._volume, value);
        }
        if (this.ThrowAfterSend || this.ThrowOnSendAttempt == attempt)
        {
            throw new IOException("Simulated response loss after a write attempt.");
        }
        if (attempt == 1 && this.HoldFirstSendCompletion)
        {
            this._firstSendReached.TrySetResult(true);
            await this._continueFirstSend.Task.WaitAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public Task<bool?> ConfirmAsync(string action, MediaTargetContext context, MediaWriteInputs inputs,
        CancellationToken cancellationToken) => Task.FromResult<bool?>(this.CurrentVolume == inputs.Value);
}
