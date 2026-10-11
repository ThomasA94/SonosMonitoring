using SonosMonitoring.Application.MediaControl;

namespace SonosMonitoring.Unit.Tests.MediaControl;

/// <summary>Tracks the durable execution decisions used by media execution unit scenarios.</summary>
internal sealed class TestMediaCoordinationStore(CoordinatedMediaOperation operation) : IMediaCoordinationStore
{
    /// <summary>Gets the latest technical operation record.</summary>
    public CoordinatedMediaOperation Operation { get; private set; } = operation;

    /// <summary>Gets the number of irreversible step releases.</summary>
    public int ReleaseCount { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, long>> EpochsAsync(string[] targets, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, long>>(new Dictionary<string, long>());

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, long>> CountsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, long>>(new Dictionary<string, long>());

    /// <inheritdoc />
    public Task<IReadOnlyList<CoordinatedMediaOperation>> UnrecordedAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CoordinatedMediaOperation>>([]);

    /// <inheritdoc />
    public Task MarkRecordedAsync(Guid id, string status, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<MediaGrantStatus> StatusAsync(CancellationToken ct) =>
        Task.FromResult(new MediaGrantStatus(true, this.Operation.Generation, []));

    /// <inheritdoc />
    public Task<MediaGrantStatus> SetGrantAsync(bool enabled, Guid requestId, string expectedGeneration, CancellationToken ct) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task<CoordinatedMediaOperation> AdmitAsync(CoordinatedMediaOperation admitted, CancellationToken ct)
    {
        this.Operation = admitted;
        return Task.FromResult(admitted);
    }

    /// <inheritdoc />
    public Task<CoordinatedMediaOperation?> CloseAdmissionAsync(CoordinatedMediaOperation admission, CancellationToken ct) =>
        Task.FromResult<CoordinatedMediaOperation?>(admission.OperationId == this.Operation.OperationId ? this.Operation : null);

    /// <inheritdoc />
    public Task<CoordinatedMediaOperation?> FindAsync(Guid id, CancellationToken ct) =>
        Task.FromResult<CoordinatedMediaOperation?>(id == this.Operation.OperationId ? this.Operation : null);

    /// <inheritdoc />
    public Task<IReadOnlyList<CoordinatedMediaOperation>> PendingAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CoordinatedMediaOperation>>([this.Operation]);

    /// <inheritdoc />
    public Task<bool> IsReadyAsync(CoordinatedMediaOperation admitted, CancellationToken ct) => Task.FromResult(true);

    /// <inheritdoc />
    public Task<bool> ReleaseAsync(Guid id, int step, CancellationToken ct)
    {
        this.ReleaseCount++;
        this.Operation = this.Operation with { Status = "dispatch_started", Step = step };
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task FinishAsync(Guid id, string status, string? code, CancellationToken ct)
    {
        this.Operation = this.Operation with { Status = status, Code = code };
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<MediaGrantStatus> AcceptRiskAsync(Guid[] ids, Guid requestId, CancellationToken ct) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task<IAsyncDisposable?> AcquireExecutorAsync(CancellationToken ct) => Task.FromResult<IAsyncDisposable?>(null);

    /// <inheritdoc />
    public Task<bool> OwnsExecutorAsync(CancellationToken ct) => Task.FromResult(true);

    /// <inheritdoc />
    public Task RecoverAsync(CancellationToken ct) => Task.CompletedTask;
}
