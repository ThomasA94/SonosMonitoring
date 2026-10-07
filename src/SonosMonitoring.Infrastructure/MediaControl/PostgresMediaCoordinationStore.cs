using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using SonosMonitoring.Application.MediaControl;

namespace SonosMonitoring.Infrastructure.MediaControl;

/// <summary>Serializes short coordination transactions while leaving all speaker I/O outside database locks.</summary>
public sealed class PostgresMediaCoordinationStore : IMediaCoordinationStore
{
    private const long EXECUTOR_LOCK = 731905240013;
    private readonly MediaControlOptions _options;
    private MediaExecutorSession? _session;

    /// <summary>Receives validated connection and resource limits.</summary>
    /// <param name="options">Explicit installation configuration.</param>
    public PostgresMediaCoordinationStore(MediaControlOptions options) => this._options = options;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, long>> EpochsAsync(string[] targets, CancellationToken ct)
    {
        await using var connection = await this.OpenAsync(ct);
        await using var command = Create(connection, null,
            "SELECT t.device_key,coalesce(e.epoch,0) FROM unnest(@targets) t(device_key) LEFT JOIN sonos_media.target_epochs e USING(device_key)",
            ("targets", targets));
        await using var reader = await command.ExecuteReaderAsync(ct);
        Dictionary<string, long> values = new(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
        {
            values[reader.GetString(0)] = reader.GetInt64(1);
        }
        return values;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, long>> CountsAsync(CancellationToken ct)
    {
        await using var connection = await this.OpenAsync(ct);
        await using var command = Create(connection, null,
            "SELECT status,count(*) FROM sonos_media.operations GROUP BY status");
        await using var reader = await command.ExecuteReaderAsync(ct);
        Dictionary<string, long> values = new(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
        {
            string status = reader.GetString(0);
            if (status is "accepted" or "dispatch_started" or "device_acknowledged" or "completed" or
                "desired_state_observed" or "rejected" or "expired" or "revoked" or "outcome_unknown")
            {
                values.Add(status, reader.GetInt64(1));
            }
        }
        return values;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CoordinatedMediaOperation>> UnrecordedAsync(CancellationToken ct)
    {
        await using var connection = await this.OpenAsync(ct);
        return await ReadOperationsAsync(connection, null,
            "WHERE outcome_recorded=false AND status IN ('outcome_unknown','revoked','expired') ORDER BY position LIMIT 32", ct);
    }

    /// <inheritdoc />
    public async Task MarkRecordedAsync(Guid id, string status, CancellationToken ct)
    {
        await using var connection = await this.OpenAsync(ct);
        await using var command = Create(connection, null,
            "UPDATE sonos_media.operations SET outcome_recorded=true WHERE id=@id AND status=@status", ("id", id), ("status", status));
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public async Task<MediaGrantStatus> StatusAsync(CancellationToken ct)
    {
        await using var connection = await this.OpenAsync(ct);
        await using var command = Create(connection, null, "SELECT enabled,generation FROM sonos_media.control WHERE singleton");
        bool enabled;
        string generation;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            enabled = reader.GetBoolean(0);
            generation = reader.GetGuid(1).ToString("D");
        }
        var blocks = await ReadOperationsAsync(connection, null,
            "WHERE (status IN ('outcome_unknown','dispatch_started') AND risk_released=false) ORDER BY position", ct);
        return new(enabled, generation, blocks);
    }

    /// <inheritdoc />
    public async Task<MediaGrantStatus> SetGrantAsync(bool enabled, Guid requestId, string expectedGeneration, CancellationToken ct)
    {
        await using var connection = await this.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, transaction, ct);
        if (await DecisionAsync(connection, transaction, requestId, enabled ? "enable" : "disable", ct))
        {
            await using var expected = Create(connection, transaction,
                "SELECT generation=@generation FROM sonos_media.control WHERE singleton", ("generation", Guid.Parse(expectedGeneration)));
            if ((bool)(await expected.ExecuteScalarAsync(ct) ?? false) == false)
            {
                throw new MediaCoordinationException("media.grant.stale_decision");
            }
            await using var command = Create(connection, transaction, """
                UPDATE sonos_media.control SET enabled=@enabled, generation=gen_random_uuid() WHERE singleton;
                UPDATE sonos_media.operations SET status='revoked', code='media.grant.revoked',outcome_recorded=false
                WHERE status IN ('accepted','device_acknowledged');
                """, ("enabled", enabled));
            await command.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return await this.StatusAsync(ct);
    }

    /// <inheritdoc />
    public async Task<CoordinatedMediaOperation> AdmitAsync(CoordinatedMediaOperation operation, CancellationToken ct)
    {
        await using var connection = await this.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, transaction, ct);
        var previous = await ReadOperationAsync(connection, transaction, operation.OperationId, ct);
        if (previous is not null)
        {
            if (previous.Fingerprint != operation.Fingerprint)
            {
                throw new MediaCoordinationException("media.idempotency.conflict");
            }
            await transaction.CommitAsync(ct);
            return previous;
        }
        await using (var authorization = Create(connection, transaction,
            "SELECT enabled AND generation=@generation AND @expires > clock_timestamp() FROM sonos_media.control WHERE singleton",
            ("generation", Guid.Parse(operation.Generation)), ("expires", operation.ExpiresAtUtc)))
        {
            if ((bool)(await authorization.ExecuteScalarAsync(ct) ?? false) == false)
            {
                throw new MediaCoordinationException("media.grant.revoked");
            }
        }
        await using (var count = Create(connection, transaction, "SELECT count(*) FROM sonos_media.operations WHERE status IN ('accepted','dispatch_started','device_acknowledged')"))
        {
            if (Convert.ToInt64(await count.ExecuteScalarAsync(ct)) >= this._options.MaxPending)
            {
                throw new MediaCoordinationException("media.queue.full", 429);
            }
        }
        using (var payload = JsonDocument.Parse(operation.PayloadJson))
        {
            JsonElement epochs = payload.RootElement.TryGetProperty("TargetEpochs", out var captured) ? captured : default;
            await using var query = Create(connection, transaction,
                "SELECT device_key,epoch FROM sonos_media.target_epochs WHERE device_key=ANY(@targets)", ("targets", operation.Targets));
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                string key = reader.GetString(0);
                if (epochs.ValueKind != JsonValueKind.Object || epochs.TryGetProperty(key, out var epoch) == false ||
                    epoch.GetInt64() != reader.GetInt64(1))
                {
                    throw new MediaCoordinationException("media.risk.old_work_invalidated");
                }
            }
        }
        await using (var blocked = Create(connection, transaction,
            "SELECT EXISTS(SELECT 1 FROM sonos_media.operations WHERE status='outcome_unknown' AND risk_released=false AND targets && @targets)",
            ("targets", operation.Targets)))
        {
            if ((bool)(await blocked.ExecuteScalarAsync(ct) ?? false))
            {
                throw new MediaCoordinationException("media.target.blocked");
            }
        }
        await using (var insert = Create(connection, transaction, """
            INSERT INTO sonos_media.operations(id,fingerprint,generation,targets,payload,accepted_at,expires_at)
            VALUES(@id,@fingerprint,@generation,@targets,CAST(@payload AS jsonb),@accepted,@expires)
            """, ("id", operation.OperationId), ("fingerprint", operation.Fingerprint), ("generation", Guid.Parse(operation.Generation)),
            ("targets", operation.Targets), ("payload", operation.PayloadJson), ("accepted", operation.AcceptedAtUtc), ("expires", operation.ExpiresAtUtc)))
        {
            await insert.ExecuteNonQueryAsync(ct);
        }
        var admitted = (await ReadOperationAsync(connection, transaction, operation.OperationId, ct))!;
        await transaction.CommitAsync(ct);
        return admitted;
    }

    /// <inheritdoc />
    public async Task<CoordinatedMediaOperation?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await this.OpenAsync(ct);
        return await ReadOperationAsync(connection, null, id, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CoordinatedMediaOperation>> PendingAsync(CancellationToken ct)
    {
        await using var connection = await this.OpenAsync(ct);
        return await ReadOperationsAsync(connection, null, "WHERE status='accepted' ORDER BY position", ct);
    }

    /// <inheritdoc />
    public async Task<bool> IsReadyAsync(CoordinatedMediaOperation operation, CancellationToken ct)
    {
        await using var connection = await this.OpenAsync(ct);
        await using var command = Create(connection, null, """
            SELECT NOT EXISTS(SELECT 1 FROM sonos_media.operations WHERE id<>@id AND targets && @targets AND
            ((status IN ('outcome_unknown','dispatch_started') AND risk_released=false) OR
            (position<@position AND status IN ('accepted','device_acknowledged'))))
            """, ("id", operation.OperationId), ("targets", operation.Targets), ("position", operation.Position));
        return (bool)(await command.ExecuteScalarAsync(ct) ?? false);
    }

    /// <inheritdoc />
    public async Task<bool> ReleaseAsync(Guid id, int step, CancellationToken ct)
    {
        if (await this.OwnsExecutorAsync(ct) == false)
        {
            return false;
        }
        // Commit on the ownership connection: losing that session rolls back this release transaction.
        var connection = this._session!.Connection;
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, transaction, ct);
        var operation = await ReadOperationAsync(connection, transaction, id, ct);
        if (operation is null || operation.Step + 1 != step || operation.Status is not ("accepted" or "device_acknowledged"))
        {
            return false;
        }
        await using (var authorization = Create(connection, transaction,
            "SELECT enabled AND generation=@generation AND @expires > clock_timestamp() FROM sonos_media.control WHERE singleton",
            ("generation", Guid.Parse(operation.Generation)), ("expires", operation.ExpiresAtUtc)))
        {
            if ((bool)(await authorization.ExecuteScalarAsync(ct) ?? false) == false)
            {
                await UpdateAsync(connection, transaction, id, "revoked", "media.grant.revoked", ct);
                await transaction.CommitAsync(ct);
                return false;
            }
        }
        await using (var blockers = Create(connection, transaction, """
            SELECT EXISTS(SELECT 1 FROM sonos_media.operations WHERE id<>@id AND targets && @targets AND
            ((status IN ('outcome_unknown','dispatch_started') AND risk_released=false) OR
            (position<@position AND status IN ('accepted','device_acknowledged'))))
            """, ("id", id), ("targets", operation.Targets), ("position", operation.Position)))
        {
            if ((bool)(await blockers.ExecuteScalarAsync(ct) ?? false))
            {
                return false;
            }
        }
        await using (var release = Create(connection, transaction, """
            INSERT INTO sonos_media.steps(operation,step) VALUES(@id,@step);
            UPDATE sonos_media.operations SET step=@step,status='dispatch_started' WHERE id=@id
            """, ("id", id), ("step", step)))
        {
            await release.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public async Task FinishAsync(Guid id, string status, string? code, CancellationToken ct)
    {
        if (status is not ("device_acknowledged" or "completed" or "desired_state_observed" or "rejected" or "expired" or "revoked" or "outcome_unknown"))
        {
            throw new ArgumentException("Invalid execution result.", nameof(status));
        }
        await using var connection = await this.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, transaction, ct);
        await UpdateAsync(connection, transaction, id, status, code, ct);
        await transaction.CommitAsync(ct);
    }

    /// <inheritdoc />
    public async Task<MediaGrantStatus> AcceptRiskAsync(Guid[] ids, Guid requestId, CancellationToken ct)
    {
        if (ids.Length is < 1 or > 256 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
        {
            throw new MediaCoordinationException("media.risk.invalid", 400);
        }
        string fingerprint = "risk:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',', ids.Order()))));
        await using var connection = await this.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, transaction, ct);
        if (await DecisionAsync(connection, transaction, requestId, fingerprint, ct))
        {
            await using var query = Create(connection, transaction,
                "SELECT targets FROM sonos_media.operations WHERE id=ANY(@ids) AND status='outcome_unknown' AND risk_released=false",
                ("ids", ids));
            List<string> targets = [];
            int count = 0;
            await using (var reader = await query.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    count++;
                    targets.AddRange(reader.GetFieldValue<string[]>(0));
                }
            }
            if (count != ids.Length)
            {
                throw new MediaCoordinationException("media.risk.not_uncertain");
            }
            await using var update = Create(connection, transaction, """
                UPDATE sonos_media.operations SET risk_released=true WHERE id=ANY(@ids);
                INSERT INTO sonos_media.target_epochs(device_key,epoch) SELECT DISTINCT unnest(@targets),1
                ON CONFLICT(device_key) DO UPDATE SET epoch=sonos_media.target_epochs.epoch+1;
                UPDATE sonos_media.operations SET status='revoked',code='media.risk.old_work_invalidated',outcome_recorded=false
                WHERE status IN ('accepted','device_acknowledged') AND targets && @targets;
                """, ("ids", ids), ("targets", targets.Distinct().ToArray()));
            await update.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return await this.StatusAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable?> AcquireExecutorAsync(CancellationToken ct)
    {
        var builder = new NpgsqlConnectionStringBuilder(this._options.ConnectionString) { Pooling = false };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            await using var command = Create(connection, null, "SELECT pg_try_advisory_lock(@lock)", ("lock", EXECUTOR_LOCK));
            if ((bool)(await command.ExecuteScalarAsync(ct) ?? false) == false)
            {
                await connection.DisposeAsync();
                return null;
            }
            this._session = new MediaExecutorSession(connection);
            return this._session;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> OwnsExecutorAsync(CancellationToken ct)
    {
        var session = this._session;
        if (session is null || session.Connection.State != ConnectionState.Open)
        {
            return false;
        }
        await using var command = Create(session.Connection, null, "SELECT 1");
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 1;
    }

    /// <inheritdoc />
    public async Task RecoverAsync(CancellationToken ct)
    {
        await using var connection = await this.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, transaction, ct);
        await using var command = Create(connection, transaction, """
            UPDATE sonos_media.operations SET status='outcome_unknown',code='media.executor.interrupted',outcome_recorded=false
            WHERE status IN ('dispatch_started','device_acknowledged');
            UPDATE sonos_media.operations SET status='expired',code='media.operation.expired',outcome_recorded=false
            WHERE status='accepted' AND expires_at<=clock_timestamp();
            """);
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>Opens a bounded connection and verifies the explicit schema version.</summary>
    /// <param name="ct">Bounded cancellation.</param>
    /// <returns>Owned verified connection.</returns>
    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(this._options.ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            await using var command = Create(connection, null, "SELECT version FROM sonos_media.version WHERE singleton");
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 1)
            {
                throw new MediaCoordinationException("media.schema.incompatible", 503);
            }
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>Serializes short generation and queue mutations without holding a lock during device I/O.</summary>
    /// <param name="connection">Transaction connection.</param>
    /// <param name="transaction">Atomic mutation.</param>
    /// <param name="ct">Bounded cancellation.</param>
    private static async Task LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var command = Create(connection, transaction, "SELECT singleton FROM sonos_media.control WHERE singleton FOR UPDATE");
        await command.ExecuteScalarAsync(ct);
    }

    /// <summary>Reserves an operator decision identity and rejects altered reuse.</summary>
    /// <param name="connection">Transaction connection.</param>
    /// <param name="transaction">Atomic mutation.</param>
    /// <param name="id">Operator decision identity.</param>
    /// <param name="fingerprint">Exact decision meaning.</param>
    /// <param name="ct">Bounded cancellation.</param>
    /// <returns>Whether the decision is new.</returns>
    private static async Task<bool> DecisionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, string fingerprint, CancellationToken ct)
    {
        await using var read = Create(connection, transaction, "SELECT fingerprint FROM sonos_media.decisions WHERE id=@id", ("id", id));
        var previous = await read.ExecuteScalarAsync(ct) as string;
        if (previous is not null)
        {
            if (previous != fingerprint)
            {
                throw new MediaCoordinationException("media.idempotency.conflict");
            }
            return false;
        }
        await using var insert = Create(connection, transaction,
            "INSERT INTO sonos_media.decisions(id,fingerprint) VALUES(@id,@fingerprint)", ("id", id), ("fingerprint", fingerprint));
        await insert.ExecuteNonQueryAsync(ct);
        return true;
    }

    /// <summary>Stores an execution result without clearing a previously persisted uncertainty fence.</summary>
    /// <param name="connection">Transaction connection.</param>
    /// <param name="transaction">Atomic mutation.</param>
    /// <param name="id">Operation identity.</param>
    /// <param name="status">New technical outcome.</param>
    /// <param name="code">Safe outcome code.</param>
    /// <param name="ct">Bounded cancellation.</param>
    private static async Task UpdateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, string status, string? code, CancellationToken ct)
    {
        await using var command = Create(connection, transaction,
            "UPDATE sonos_media.operations SET status=@status,code=@code WHERE id=@id AND status NOT IN ('outcome_unknown','revoked','expired','desired_state_observed','completed','rejected')",
            ("id", id), ("status", status), ("code", (object?)code ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Reads a complete immutable coordination record.</summary>
    /// <param name="connection">Owned read connection.</param>
    /// <param name="transaction">Optional mutation transaction.</param>
    /// <param name="id">Operation identity.</param>
    /// <param name="ct">Bounded cancellation.</param>
    /// <returns>Existing admission, if present.</returns>
    private static async Task<CoordinatedMediaOperation?> ReadOperationAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid id, CancellationToken ct)
    {
        var values = await ReadOperationsAsync(connection, transaction, "WHERE id=@id", ct, ("id", id));
        return values.SingleOrDefault();
    }

    /// <summary>Reads a bounded query using application-owned SQL and parameterized values.</summary>
    /// <param name="connection">Owned read connection.</param>
    /// <param name="transaction">Optional mutation transaction.</param>
    /// <param name="suffix">Application-owned query predicate.</param>
    /// <param name="ct">Bounded cancellation.</param>
    /// <param name="parameters">Typed values.</param>
    /// <returns>Complete coordination records.</returns>
    private static async Task<IReadOnlyList<CoordinatedMediaOperation>> ReadOperationsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string suffix, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var command = Create(connection, transaction,
            "SELECT id,position,fingerprint,generation,targets,payload::text,accepted_at,expires_at,status,step,code FROM sonos_media.operations " + suffix, parameters);
        await using var reader = await command.ExecuteReaderAsync(ct);
        List<CoordinatedMediaOperation> values = [];
        while (await reader.ReadAsync(ct))
        {
            values.Add(new(reader.GetGuid(0), reader.GetInt64(1), reader.GetString(2), reader.GetGuid(3).ToString("D"),
                reader.GetFieldValue<string[]>(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetString(8), reader.GetInt32(9), reader.IsDBNull(10) ? null : reader.GetString(10)));
        }
        return values;
    }

    /// <summary>Constructs a parameterized command with a fixed database timeout.</summary>
    /// <param name="connection">Owned connection.</param>
    /// <param name="transaction">Optional transaction.</param>
    /// <param name="sql">Application-owned SQL.</param>
    /// <param name="parameters">Typed command values.</param>
    /// <returns>An owned command.</returns>
    private static NpgsqlCommand Create(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 5 };
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
        return command;
    }
}
