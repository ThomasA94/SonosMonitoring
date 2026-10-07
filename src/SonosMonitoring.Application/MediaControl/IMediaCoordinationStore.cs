namespace SonosMonitoring.Application.MediaControl;

/// <summary>Owns durable target order, permission generations, irreversible dispatch and uncertainty fences.</summary>
public interface IMediaCoordinationStore
{
    /// <summary>Captures target-specific risk decision epochs before committing an intention.</summary>
    /// <param name="targets">Immutable conflict identities.</param>
    /// <param name="ct">Query cancellation.</param>
    /// <returns>Current epochs, including zero for targets without a risk decision.</returns>
    Task<IReadOnlyDictionary<string, long>> EpochsAsync(string[] targets, CancellationToken ct);
    /// <summary>Counts retained operations by bounded phase for installation metrics.</summary>
    /// <param name="ct">Scrape cancellation.</param>
    /// <returns>Counts with fixed status keys and no player or operation labels.</returns>
    Task<IReadOnlyDictionary<string, long>> CountsAsync(CancellationToken ct);

    /// <summary>Reads terminal coordination decisions whose semantic audit transition remains pending.</summary>
    /// <param name="ct">Query cancellation.</param>
    /// <returns>Bounded pending audit transitions.</returns>
    Task<IReadOnlyList<CoordinatedMediaOperation>> UnrecordedAsync(CancellationToken ct);

    /// <summary>Marks a technical outcome as also retained in its operation stream.</summary>
    /// <param name="id">Operation identity.</param>
    /// <param name="status">Exact coordination outcome retained by the audit transition.</param>
    /// <param name="ct">Mutation cancellation.</param>
    Task MarkRecordedAsync(Guid id, string status, CancellationToken ct);

    /// <summary>Reads authoritative control permission and persistent uncertainty.</summary>
    /// <param name="ct">Bounded cancellation.</param>
    Task<MediaGrantStatus> StatusAsync(CancellationToken ct);
    /// <summary>Changes permission idempotently; revocation permanently invalidates unstarted old work.</summary>
    /// <param name="enabled">Explicit desired permission.</param>
    /// <param name="requestId">Stable control action identity.</param>
    /// <param name="expectedGeneration">Original generation captured when the decision was committed.</param>
    /// <param name="ct">Bounded cancellation.</param>
    Task<MediaGrantStatus> SetGrantAsync(bool enabled, Guid requestId, string expectedGeneration, CancellationToken ct);
    /// <summary>Atomically admits an immutable operation on its entire conflict target set.</summary>
    /// <param name="operation">Original domain-committed descriptor.</param>
    /// <param name="ct">Bounded cancellation.</param>
    Task<CoordinatedMediaOperation> AdmitAsync(CoordinatedMediaOperation operation, CancellationToken ct);
    /// <summary>Reads an existing admission without changing its identity or generation.</summary>
    /// <param name="id">Operation identity.</param>
    /// <param name="ct">Bounded cancellation.</param>
    Task<CoordinatedMediaOperation?> FindAsync(Guid id, CancellationToken ct);
    /// <summary>Reads ordered pending work; arrival notifications never determine order.</summary>
    /// <param name="ct">Bounded cancellation.</param>
    Task<IReadOnlyList<CoordinatedMediaOperation>> PendingAsync(CancellationToken ct);
    /// <summary>Checks head position without granting an irreversible write; release checks it again atomically.</summary>
    /// <param name="operation">Original admitted operation.</param>
    /// <param name="ct">Bounded cancellation.</param>
    Task<bool> IsReadyAsync(CoordinatedMediaOperation operation, CancellationToken ct);
    /// <summary>Atomically authorizes exactly one write step against generation, target order and fences.</summary>
    /// <param name="id">Admitted operation identity.</param>
    /// <param name="step">Sequential write step.</param>
    /// <param name="ct">Bounded cancellation.</param>
    Task<bool> ReleaseAsync(Guid id, int step, CancellationToken ct);
    /// <summary>Persists a technical result; uncertainty keeps the targets fenced.</summary>
    /// <param name="id">Admitted operation.</param>
    /// <param name="status">Result status.</param>
    /// <param name="code">Safe outcome code.</param>
    /// <param name="ct">Bounded cancellation.</param>
    Task FinishAsync(Guid id, string status, string? code, CancellationToken ct);
    /// <summary>Explicitly accepts delayed-effect risk and invalidates prior waiting work.</summary>
    /// <param name="ids">Exact uncertain operations.</param>
    /// <param name="requestId">Stable operator decision identity.</param>
    /// <param name="ct">Bounded cancellation.</param>
    Task<MediaGrantStatus> AcceptRiskAsync(Guid[] ids, Guid requestId, CancellationToken ct);
    /// <summary>Acquires an installation-wide executor session; no timeout transfers an in-flight request.</summary>
    /// <param name="ct">Bounded cancellation.</param>
    Task<IAsyncDisposable?> AcquireExecutorAsync(CancellationToken ct);
    /// <summary>Checks the live executor session immediately before irreversible admission.</summary>
    /// <param name="ct">Bounded cancellation.</param>
    Task<bool> OwnsExecutorAsync(CancellationToken ct);
    /// <summary>Fences interrupted dispatches before any newly started writes.</summary>
    /// <param name="ct">Bounded cancellation.</param>
    Task RecoverAsync(CancellationToken ct);
}
