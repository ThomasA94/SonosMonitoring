using Npgsql;

namespace SonosMonitoring.Infrastructure.MediaControl;

/// <summary>Holds a database-session advisory lock until the original executor has drained.</summary>
internal sealed class MediaExecutorSession : IAsyncDisposable
{
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private int _disposeStarted;

    /// <summary>Receives the exclusively owned connection.</summary>
    /// <param name="connection">Dedicated connection with pooling disabled.</param>
    public MediaExecutorSession(NpgsqlConnection connection) => this.Connection = connection;

    /// <summary>Gets the session whose lifetime fences executor ownership.</summary>
    public NpgsqlConnection Connection { get; }

    /// <summary>Serializes every operation against the session connection.</summary>
    /// <typeparam name="TResult">The value returned by the session operation.</typeparam>
    /// <param name="operation">A short ownership query or transaction.</param>
    /// <param name="ct">Operation cancellation.</param>
    /// <returns>The operation result.</returns>
    public async Task<TResult> ExecuteAsync<TResult>(Func<NpgsqlConnection, CancellationToken, Task<TResult>> operation, CancellationToken ct)
    {
        await this._connectionGate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref this._disposeStarted) != 0, this);
            return await operation(this.Connection, ct);
        }
        finally
        {
            this._connectionGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this._disposeStarted, 1) != 0)
        {
            return;
        }

        await this._connectionGate.WaitAsync(CancellationToken.None);
        try
        {
            await this.Connection.DisposeAsync();
        }
        finally
        {
            this._connectionGate.Release();
        }
    }
}
