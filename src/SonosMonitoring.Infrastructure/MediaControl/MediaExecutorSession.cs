using Npgsql;

namespace SonosMonitoring.Infrastructure.MediaControl;

/// <summary>Holds a database-session advisory lock until the original executor has drained.</summary>
internal sealed class MediaExecutorSession : IAsyncDisposable
{
    /// <summary>Receives the exclusively owned connection.</summary>
    /// <param name="connection">Dedicated connection with pooling disabled.</param>
    public MediaExecutorSession(NpgsqlConnection connection) => this.Connection = connection;
    /// <summary>Gets the session whose lifetime fences executor ownership.</summary>
    public NpgsqlConnection Connection { get; }
    /// <inheritdoc />
    public ValueTask DisposeAsync() => this.Connection.DisposeAsync();
}
