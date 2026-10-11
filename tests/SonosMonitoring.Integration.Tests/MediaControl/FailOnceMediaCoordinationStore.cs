using SonosMonitoring.Application.MediaControl;

namespace SonosMonitoring.Integration.Tests.MediaControl;

/// <summary>Injects one failed result write after the semantic event has committed.</summary>
public sealed class FailOnceMediaCoordinationStore : IMediaCoordinationStore
{
    private readonly IMediaCoordinationStore _inner;
    private readonly TaskCompletionSource<bool> _failureReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _failRejectedResult = 1;
    private int _failAdmission;

    /// <summary>Wraps the real PostgreSQL coordinator without changing its other decisions.</summary>
    /// <param name="inner">Real durable coordinator.</param>
    /// <param name="failAdmission">Fails admission instead of result persistence when true.</param>
    public FailOnceMediaCoordinationStore(IMediaCoordinationStore inner, bool failAdmission = false)
    {
        this._inner = inner;
        this._failAdmission = failAdmission ? 1 : 0;
        this._failRejectedResult = failAdmission ? 0 : 1;
    }

    /// <summary>Waits until the deliberate coordinator result failure has occurred.</summary>
    /// <param name="ct">Bounded test cancellation.</param>
    public Task WaitForFailureAsync(CancellationToken ct) => this._failureReached.Task.WaitAsync(ct);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, long>> EpochsAsync(string[] targets, CancellationToken ct) =>
        this._inner.EpochsAsync(targets, ct);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, long>> CountsAsync(CancellationToken ct) => this._inner.CountsAsync(ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<CoordinatedMediaOperation>> UnrecordedAsync(CancellationToken ct) =>
        this._inner.UnrecordedAsync(ct);

    /// <inheritdoc />
    public Task MarkRecordedAsync(Guid id, string status, CancellationToken ct) =>
        this._inner.MarkRecordedAsync(id, status, ct);

    /// <inheritdoc />
    public Task<MediaGrantStatus> StatusAsync(CancellationToken ct) => this._inner.StatusAsync(ct);

    /// <inheritdoc />
    public Task<MediaGrantStatus> SetGrantAsync(bool enabled, Guid requestId, string expectedGeneration, CancellationToken ct) =>
        this._inner.SetGrantAsync(enabled, requestId, expectedGeneration, ct);

    /// <inheritdoc />
    public Task<CoordinatedMediaOperation> AdmitAsync(CoordinatedMediaOperation operation, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref this._failAdmission, 0) == 1)
        {
            this._failureReached.TrySetResult(true);
            throw new IOException("Simulated admission outage after domain commit.");
        }
        return this._inner.AdmitAsync(operation, ct);
    }

    /// <inheritdoc />
    public Task<CoordinatedMediaOperation?> FindAsync(Guid id, CancellationToken ct) => this._inner.FindAsync(id, ct);

    /// <inheritdoc />
    public Task<CoordinatedMediaOperation?> CloseAdmissionAsync(CoordinatedMediaOperation operation, CancellationToken ct) =>
        this._inner.CloseAdmissionAsync(operation, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<CoordinatedMediaOperation>> PendingAsync(CancellationToken ct) => this._inner.PendingAsync(ct);

    /// <inheritdoc />
    public Task<bool> IsReadyAsync(CoordinatedMediaOperation operation, CancellationToken ct) =>
        this._inner.IsReadyAsync(operation, ct);

    /// <inheritdoc />
    public Task<bool> ReleaseAsync(Guid id, int step, CancellationToken ct) => this._inner.ReleaseAsync(id, step, ct);

    /// <inheritdoc />
    public Task FinishAsync(Guid id, string status, string? code, CancellationToken ct)
    {
        if (status == "rejected" && Interlocked.Exchange(ref this._failRejectedResult, 0) == 1)
        {
            this._failureReached.TrySetResult(true);
            throw new IOException("Simulated result persistence outage after domain commit.");
        }
        return this._inner.FinishAsync(id, status, code, ct);
    }

    /// <inheritdoc />
    public Task<MediaGrantStatus> AcceptRiskAsync(Guid[] ids, Guid requestId, CancellationToken ct) =>
        this._inner.AcceptRiskAsync(ids, requestId, ct);

    /// <inheritdoc />
    public Task<IAsyncDisposable?> AcquireExecutorAsync(CancellationToken ct) => this._inner.AcquireExecutorAsync(ct);

    /// <inheritdoc />
    public Task<bool> OwnsExecutorAsync(CancellationToken ct) => this._inner.OwnsExecutorAsync(ct);

    /// <inheritdoc />
    public Task RecoverAsync(CancellationToken ct) => this._inner.RecoverAsync(ct);
}
