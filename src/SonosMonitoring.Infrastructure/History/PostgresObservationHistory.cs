using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using SonosMonitoring.Application.History;

namespace SonosMonitoring.Infrastructure.History;

/// <summary>Stores sampled observation transitions in PostgreSQL, separate from authoritative domain events.</summary>
public sealed class PostgresObservationHistory : IObservationHistory
{
    private const int CONNECTION_AND_COMMAND_TIMEOUT_SECONDS = 5;
    private const int OPERATION_TIMEOUT_SECONDS = 10;
    private const int MAX_CHECKPOINTS = ObservationHistoryLimits.MAX_KNOWN_DEVICES;
    private const int MAX_STATE_BYTES = 256 * 1024;
    private const int MAX_PAGE_STATE_BYTES = 4 * 1024 * 1024;
    private const long ADVISORY_LOCK_KEY = 746192058331L;
    private const int SCHEMA_VERSION = 1;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly string _connectionString;
    private readonly int _retentionDays;
    private readonly int _maxEntries;
    private bool _hasCompletedInitialWrite;

    /// <summary>Creates a journal adapter from deployment-supplied connection and retention settings.</summary>
    /// <param name="options">Validated observation-history options.</param>
    public PostgresObservationHistory(ObservationHistoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new ArgumentException("Observation history requires a PostgreSQL connection string.", nameof(options));
        }
        NpgsqlConnectionStringBuilder builder = new(options.ConnectionString)
        {
            Timeout = CONNECTION_AND_COMMAND_TIMEOUT_SECONDS,
            CommandTimeout = CONNECTION_AND_COMMAND_TIMEOUT_SECONDS
        };
        this._connectionString = builder.ConnectionString;
        this._retentionDays = options.RetentionDays;
        this._maxEntries = options.MaxEntries;
    }

    /// <summary>Verifies the explicit schema version and required tables without creating or migrating them.</summary>
    /// <param name="ct">Startup or caller cancellation.</param>
    public async Task VerifyAsync(CancellationToken ct)
    {
        using CancellationTokenSource budget = this.CreateBudget(ct);
        await using NpgsqlConnection connection = await this.OpenConnectionAsync(budget.Token);
        await this.VerifySchemaAsync(connection, null, budget.Token);
    }

    /// <summary>Records one normalized collector sample and any coverage transitions atomically.</summary>
    /// <param name="runId">Stable identifier for this collector process run.</param>
    /// <param name="capturedAtUtc">Journal sample time.</param>
    /// <param name="collectorObserving">Whether source evidence is currently observable.</param>
    /// <param name="samples">Bounded normalized device observations.</param>
    /// <param name="storageGap">Whether a prior persistence failure left an explicit coverage gap.</param>
    /// <param name="ct">Worker cancellation.</param>
    public async Task WriteAsync(Guid runId, DateTimeOffset capturedAtUtc, bool collectorObserving,
        IReadOnlyList<ObservationSample> samples, bool storageGap, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(samples);
        string[] serializedStates = this.ValidateAndSerializeSamples(samples);
        DateTimeOffset recordedAtUtc = capturedAtUtc.ToUniversalTime();
        using CancellationTokenSource budget = this.CreateBudget(ct);
        await using NpgsqlConnection connection = await this.OpenConnectionAsync(budget.Token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(budget.Token);
        await this.AcquireWriterLockAsync(connection, transaction, budget.Token);
        await this.VerifySchemaAsync(connection, transaction, budget.Token);

        (Guid? previousRun, bool? previousObserving) = await this.ReadMetaAsync(connection, transaction, budget.Token);
        if (previousRun != runId)
        {
            await this.InsertGapAsync(connection, transaction, recordedAtUtc, "collector_restart", budget.Token);
        }
        if (storageGap)
        {
            await this.InsertGapAsync(connection, transaction, recordedAtUtc, "storage_unavailable", budget.Token);
        }
        if (previousObserving is not null && previousObserving != collectorObserving)
        {
            string reason = collectorObserving ? "collector_resumed" : "collector_unavailable";
            await this.InsertGapAsync(connection, transaction, recordedAtUtc, reason, budget.Token);
        }

        bool hasInsertedEntry = previousRun != runId || storageGap ||
            (previousObserving is not null && previousObserving != collectorObserving);
        for (int index = 0; index < samples.Count; index++)
        {
            hasInsertedEntry |= await this.WriteSampleAsync(connection, transaction, samples[index], serializedStates[index], recordedAtUtc, budget.Token);
        }

        await this.UpsertMetaAsync(connection, transaction, runId, recordedAtUtc, collectorObserving, budget.Token);
        await this.ApplyRetentionAsync(connection, transaction, recordedAtUtc,
            hasInsertedEntry || this._hasCompletedInitialWrite == false, budget.Token);
        await transaction.CommitAsync(budget.Token);
        this._hasCompletedInitialWrite = true;
    }

    /// <inheritdoc />
    public async Task<ObservationPage> ReadAsync(long after, int limit, string? deviceId, CancellationToken cancellationToken)
    {
        if (after < 0 || limit is < 1 or > 200 ||
            (deviceId is not null && (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 128)))
        {
            throw new ArgumentException("Observation history query bounds or device identity are invalid.");
        }

        using CancellationTokenSource budget = this.CreateBudget(cancellationToken);
        await using NpgsqlConnection connection = await this.OpenConnectionAsync(budget.Token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, budget.Token);
        await this.VerifySchemaAsync(connection, transaction, budget.Token);
        long? earliestCursor = await this.ReadEarliestCursorAsync(connection, transaction, budget.Token);
        DateTimeOffset? lastRecordedAtUtc = await this.ReadLastRecordedAtAsync(connection, transaction, budget.Token);
        List<ObservationChange> changes = [];
        int stateBytes = 0;
        const string SQL = """
            SELECT sequence, kind, recorded_at, observed_at, device_id, before_state::text, after_state::text, reason
            FROM sonos_history.entries
            WHERE sequence > @after
              AND (@device_id IS NULL OR device_id = @device_id OR (device_id IS NULL AND kind = 'gap'))
            ORDER BY sequence
            LIMIT @limit
            """;
        await using (NpgsqlCommand command = this.CreateCommand(connection, transaction, SQL))
        {
            command.Parameters.AddWithValue("after", NpgsqlDbType.Bigint, after);
            command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
            command.Parameters.AddWithValue("device_id", NpgsqlDbType.Varchar, (object?)deviceId ?? DBNull.Value);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(budget.Token);
            while (await reader.ReadAsync(budget.Token))
            {
                string? beforeJson = reader.IsDBNull(5) ? null : reader.GetString(5);
                string? afterJson = reader.IsDBNull(6) ? null : reader.GetString(6);
                int entryBytes = this.GetStateBytes(beforeJson) + this.GetStateBytes(afterJson);
                if (changes.Count > 0 && stateBytes + entryBytes > MAX_PAGE_STATE_BYTES)
                {
                    break;
                }
                stateBytes += entryBytes;
                changes.Add(new ObservationChange(reader.GetInt64(0), reader.GetString(1), this.ReadUtc(reader, 2),
                    reader.IsDBNull(3) ? null : this.ReadUtc(reader, 3), this.DeserializeState(beforeJson),
                    this.DeserializeState(afterJson), reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }

        await transaction.CommitAsync(budget.Token);
        long nextCursor = changes.Count == 0 ? after : changes[^1].Sequence;
        return new ObservationPage(changes.ToArray(), nextCursor, earliestCursor, lastRecordedAtUtc,
            this._retentionDays, this._maxEntries);
    }

    /// <summary>Creates an operation timeout linked to the caller's cancellation token.</summary>
    /// <param name="ct">Caller cancellation.</param>
    /// <returns>A cancellation source bounded by the operation budget.</returns>
    private CancellationTokenSource CreateBudget(CancellationToken ct)
    {
        CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(OPERATION_TIMEOUT_SECONDS));
        return budget;
    }

    /// <summary>Opens a PostgreSQL connection while disposing it if opening fails.</summary>
    /// <param name="ct">Operation cancellation and timeout.</param>
    /// <returns>An open connection.</returns>
    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        NpgsqlConnection connection = new(this._connectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>Checks schema version and required table presence without attempting migration.</summary>
    /// <param name="connection">Open journal connection.</param>
    /// <param name="transaction">Current transaction, or null during startup verification.</param>
    /// <param name="ct">Operation cancellation and timeout.</param>
    private async Task VerifySchemaAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken ct)
    {
        const string SQL = """
            SELECT version,
                   to_regclass('sonos_history.entries') IS NOT NULL,
                   to_regclass('sonos_history.checkpoints') IS NOT NULL,
                   to_regclass('sonos_history.meta') IS NOT NULL
            FROM sonos_history.schema_version
            WHERE singleton = true
            """;
        await using NpgsqlCommand command = this.CreateCommand(connection, transaction, SQL);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct) == false || reader.GetInt32(0) != SCHEMA_VERSION ||
            reader.GetBoolean(1) == false || reader.GetBoolean(2) == false || reader.GetBoolean(3) == false ||
            await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException("Observation history schema is missing or has an unsupported version.");
        }
    }

    /// <summary>Serializes samples and rejects oversized, duplicate, or mismatched device states.</summary>
    /// <param name="samples">Proposed normalized batch.</param>
    /// <returns>JSON strings in batch order.</returns>
    private string[] ValidateAndSerializeSamples(IReadOnlyList<ObservationSample> samples)
    {
        if (samples.Count > ObservationHistoryLimits.MAX_KNOWN_DEVICES)
        {
            throw new ArgumentException("Observation batch exceeds its device limit.", nameof(samples));
        }
        HashSet<string> ids = new(StringComparer.Ordinal);
        string[] states = new string[samples.Count];
        for (int index = 0; index < samples.Count; index++)
        {
            ObservationSample sample = samples[index] ?? throw new ArgumentException("Observation batch contains a null sample.", nameof(samples));
            ObservedDevice state = sample.State ?? throw new ArgumentException("Observation batch contains a null state.", nameof(samples));
            if (string.IsNullOrEmpty(state.DeviceId) || state.DeviceId.Length > 128 ||
                Regex.IsMatch(state.DeviceId, @"\ARINCON_[A-Z0-9_]{1,121}\z", RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100)) == false || ids.Add(state.DeviceId) == false)
            {
                throw new ArgumentException("Observation batch contains an invalid or duplicate device identity.", nameof(samples));
            }
            states[index] = JsonSerializer.Serialize(state, _jsonOptions);
            if (Encoding.UTF8.GetByteCount(states[index]) > MAX_STATE_BYTES)
            {
                throw new ArgumentException("Observation state exceeds its storage limit.", nameof(samples));
            }
        }
        return states;
    }

    /// <summary>Acquires the fixed transaction-scoped lock that serializes this single collector journal.</summary>
    /// <param name="connection">Open journal connection.</param>
    /// <param name="transaction">Active journal transaction.</param>
    /// <param name="ct">Operation cancellation and timeout.</param>
    private async Task AcquireWriterLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using NpgsqlCommand command = this.CreateCommand(connection, transaction, "SELECT pg_advisory_xact_lock(@lock_key)");
        command.Parameters.AddWithValue("lock_key", NpgsqlDbType.Bigint, ADVISORY_LOCK_KEY);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Reads the prior collector run and observability state under the writer lock.</summary>
    /// <param name="connection">Open journal connection.</param>
    /// <param name="transaction">Active journal transaction.</param>
    /// <param name="ct">Operation cancellation and timeout.</param>
    /// <returns>Previous run ID and collection availability.</returns>
    private async Task<(Guid? RunId, bool? CollectorObserving)> ReadMetaAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using NpgsqlCommand command = this.CreateCommand(connection, transaction,
            "SELECT run_id, collector_observing FROM sonos_history.meta WHERE singleton = true FOR UPDATE");
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct) == false)
        {
            throw new InvalidOperationException("Observation history metadata row is missing.");
        }
        Guid? runId = reader.IsDBNull(0) ? null : reader.GetGuid(0);
        bool? observing = reader.IsDBNull(1) ? null : reader.GetBoolean(1);
        if (await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException("Observation history metadata is ambiguous.");
        }
        return (runId, observing);
    }

    /// <summary>Writes a global coverage gap with no invented device-level details.</summary>
    /// <param name="connection">Open journal connection.</param>
    /// <param name="transaction">Active journal transaction.</param>
    /// <param name="recordedAtUtc">Gap observation time.</param>
    /// <param name="reason">Allowlisted gap category.</param>
    /// <param name="ct">Operation cancellation and timeout.</param>
    private async Task InsertGapAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        DateTimeOffset recordedAtUtc, string reason, CancellationToken ct)
    {
        await using NpgsqlCommand command = this.CreateCommand(connection, transaction, """
            INSERT INTO sonos_history.entries (kind, recorded_at, reason)
            VALUES ('gap', @recorded_at, @reason)
            """);
        command.Parameters.AddWithValue("recorded_at", NpgsqlDbType.TimestampTz, recordedAtUtc);
        command.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, reason);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Compares one device with its JSONB checkpoint, appends a transition if needed, and refreshes the checkpoint.</summary>
    /// <param name="connection">Open journal connection.</param>
    /// <param name="transaction">Active journal transaction.</param>
    /// <param name="sample">Current normalized device state.</param>
    /// <param name="afterJson">Serialized normalized state.</param>
    /// <param name="recordedAtUtc">Journal sample time.</param>
    /// <param name="ct">Operation cancellation and timeout.</param>
    /// <returns>Whether this sample inserted a baseline or change entry.</returns>
    private async Task<bool> WriteSampleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ObservationSample sample,
        string afterJson, DateTimeOffset recordedAtUtc, CancellationToken ct)
    {
        string? beforeJson = null;
        bool exists = false;
        bool unchanged = false;
        const string SELECT_SQL = """
            SELECT state = @after_state::jsonb, state::text
            FROM sonos_history.checkpoints
            WHERE device_id = @device_id
            """;
        await using (NpgsqlCommand select = this.CreateCommand(connection, transaction, SELECT_SQL))
        {
            select.Parameters.AddWithValue("device_id", NpgsqlDbType.Varchar, sample.State.DeviceId);
            select.Parameters.AddWithValue("after_state", NpgsqlDbType.Jsonb, afterJson);
            await using NpgsqlDataReader reader = await select.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                exists = true;
                unchanged = reader.GetBoolean(0);
                beforeJson = reader.GetString(1);
            }
        }

        if (exists == false || unchanged == false)
        {
            string kind = exists ? "change" : "baseline";
            await this.InsertObservationAsync(connection, transaction, kind, recordedAtUtc,
                sample.ObservedAtUtc?.ToUniversalTime(), sample.State.DeviceId, exists ? beforeJson : null, afterJson, ct);
        }
        string checkpointSql = exists && unchanged
            ? "UPDATE sonos_history.checkpoints SET last_seen = @last_seen WHERE device_id = @device_id"
            : """
                INSERT INTO sonos_history.checkpoints (device_id, state, last_seen)
                VALUES (@device_id, @state::jsonb, @last_seen)
                ON CONFLICT (device_id) DO UPDATE SET state = EXCLUDED.state, last_seen = EXCLUDED.last_seen
                """;
        await using NpgsqlCommand checkpoint = this.CreateCommand(connection, transaction, checkpointSql);
        checkpoint.Parameters.AddWithValue("device_id", NpgsqlDbType.Varchar, sample.State.DeviceId);
        if (exists == false || unchanged == false)
        {
            checkpoint.Parameters.AddWithValue("state", NpgsqlDbType.Jsonb, afterJson);
        }
        checkpoint.Parameters.AddWithValue("last_seen", NpgsqlDbType.TimestampTz, recordedAtUtc);
        await checkpoint.ExecuteNonQueryAsync(ct);
        return exists == false || unchanged == false;
    }

    /// <summary>Appends a baseline or changed-state row with explicit JSONB before and after evidence.</summary>
    /// <param name="connection">Open journal connection.</param>
    /// <param name="transaction">Active journal transaction.</param>
    /// <param name="kind">Baseline or change.</param>
    /// <param name="recordedAtUtc">Journal sample time.</param>
    /// <param name="observedAtUtc">Source attempt time, if present.</param>
    /// <param name="deviceId">Validated device identity.</param>
    /// <param name="beforeJson">Prior state, null for baseline.</param>
    /// <param name="afterJson">Current state.</param>
    /// <param name="ct">Operation cancellation and timeout.</param>
    private async Task InsertObservationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string kind,
        DateTimeOffset recordedAtUtc, DateTimeOffset? observedAtUtc, string deviceId, string? beforeJson,
        string afterJson, CancellationToken ct)
    {
        await using NpgsqlCommand command = this.CreateCommand(connection, transaction, """
            INSERT INTO sonos_history.entries
                (kind, recorded_at, observed_at, device_id, before_state, after_state)
            VALUES (@kind, @recorded_at, @observed_at, @device_id, @before_state::jsonb, @after_state::jsonb)
            """);
        command.Parameters.AddWithValue("kind", NpgsqlDbType.Text, kind);
        command.Parameters.AddWithValue("recorded_at", NpgsqlDbType.TimestampTz, recordedAtUtc);
        command.Parameters.AddWithValue("observed_at", NpgsqlDbType.TimestampTz,
            (object?)observedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("device_id", NpgsqlDbType.Varchar, deviceId);
        command.Parameters.AddWithValue("before_state", NpgsqlDbType.Jsonb, (object?)beforeJson ?? DBNull.Value);
        command.Parameters.AddWithValue("after_state", NpgsqlDbType.Jsonb, afterJson);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Updates the singleton run and journal freshness metadata after successful sampling.</summary>
    /// <param name="connection">Open journal connection.</param>
    /// <param name="transaction">Active journal transaction.</param>
    /// <param name="runId">Current collector process run.</param>
    /// <param name="recordedAtUtc">Latest successful journal sample time.</param>
    /// <param name="collectorObserving">Current collection availability.</param>
    /// <param name="ct">Operation cancellation and timeout.</param>
    private async Task UpsertMetaAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid runId,
        DateTimeOffset recordedAtUtc, bool collectorObserving, CancellationToken ct)
    {
        await using NpgsqlCommand command = this.CreateCommand(connection, transaction, """
            UPDATE sonos_history.meta
            SET run_id = @run_id, last_recorded = @last_recorded, collector_observing = @collector_observing
            WHERE singleton = true
            """);
        command.Parameters.AddWithValue("run_id", NpgsqlDbType.Uuid, runId);
        command.Parameters.AddWithValue("last_recorded", NpgsqlDbType.TimestampTz, recordedAtUtc);
        command.Parameters.AddWithValue("collector_observing", NpgsqlDbType.Boolean, collectorObserving);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
        {
            throw new InvalidOperationException("Observation history metadata row is missing.");
        }
    }

    /// <summary>Enforces age, entry-count, and bounded checkpoint retention in the write transaction.</summary>
    /// <param name="connection">Open journal connection.</param>
    /// <param name="transaction">Active journal transaction.</param>
    /// <param name="recordedAtUtc">Journal sample time used as retention reference.</param>
    /// <param name="enforceEntryCount">Whether this write inserted entries or is the store's first successful write.</param>
    /// <param name="ct">Operation cancellation and timeout.</param>
    private async Task ApplyRetentionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        DateTimeOffset recordedAtUtc, bool enforceEntryCount, CancellationToken ct)
    {
        await using (NpgsqlCommand age = this.CreateCommand(connection, transaction,
            "DELETE FROM sonos_history.entries WHERE recorded_at < @cutoff"))
        {
            age.Parameters.AddWithValue("cutoff", NpgsqlDbType.TimestampTz, recordedAtUtc.AddDays(-this._retentionDays));
            await age.ExecuteNonQueryAsync(ct);
        }
        if (enforceEntryCount)
        {
            await using NpgsqlCommand count = this.CreateCommand(connection, transaction, """
                DELETE FROM sonos_history.entries
                WHERE sequence IN
                (
                    SELECT sequence FROM sonos_history.entries
                    ORDER BY sequence DESC OFFSET @max_entries
                )
                """);
            count.Parameters.AddWithValue("max_entries", NpgsqlDbType.Integer, this._maxEntries);
            await count.ExecuteNonQueryAsync(ct);
        }
        await using NpgsqlCommand checkpoints = this.CreateCommand(connection, transaction, """
            DELETE FROM sonos_history.checkpoints
            WHERE device_id IN
            (
                SELECT device_id FROM sonos_history.checkpoints
                ORDER BY last_seen DESC, device_id ASC OFFSET @max_checkpoints
            )
            """);
        checkpoints.Parameters.AddWithValue("max_checkpoints", NpgsqlDbType.Integer, MAX_CHECKPOINTS);
        await checkpoints.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Reads the earliest globally retained sequence inside the page transaction.</summary>
    /// <param name="connection">Open journal connection.</param>
    /// <param name="transaction">Repeatable-read page transaction.</param>
    /// <param name="ct">Operation cancellation and timeout.</param>
    /// <returns>Earliest retained sequence, or null for an empty journal.</returns>
    private async Task<long?> ReadEarliestCursorAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using NpgsqlCommand command = this.CreateCommand(connection, transaction,
            "SELECT MIN(sequence) FROM sonos_history.entries");
        object? value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Reads the last successful sample time from singleton metadata.</summary>
    /// <param name="connection">Open journal connection.</param>
    /// <param name="transaction">Repeatable-read page transaction.</param>
    /// <param name="ct">Operation cancellation and timeout.</param>
    /// <returns>Latest sample timestamp, or null before the first write.</returns>
    private async Task<DateTimeOffset?> ReadLastRecordedAtAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken ct)
    {
        await using NpgsqlCommand command = this.CreateCommand(connection, transaction,
            "SELECT last_recorded FROM sonos_history.meta WHERE singleton = true");
        object? value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : this.AsUtc((DateTime)value);
    }

    /// <summary>Creates a command with the required per-command timeout.</summary>
    /// <param name="connection">Open PostgreSQL connection.</param>
    /// <param name="transaction">Associated transaction, or null for standalone verification.</param>
    /// <param name="sql">Static parameterized SQL statement.</param>
    /// <returns>A configured Npgsql command.</returns>
    private NpgsqlCommand CreateCommand(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql) =>
        new(sql, connection, transaction) { CommandTimeout = CONNECTION_AND_COMMAND_TIMEOUT_SECONDS };

    /// <summary>Deserializes one stored normalized device state.</summary>
    /// <param name="json">Stored JSONB text, or null when no state exists.</param>
    /// <returns>Deserialized state, or null for absent before/after evidence.</returns>
    private ObservedDevice? DeserializeState(string? json)
    {
        if (json is null)
        {
            return null;
        }
        return JsonSerializer.Deserialize<ObservedDevice>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Observation history contains an unreadable state.");
    }

    /// <summary>Computes the serialized byte size of one optional state.</summary>
    /// <param name="json">State JSON text.</param>
    /// <returns>UTF-8 byte count, or zero when absent.</returns>
    private int GetStateBytes(string? json) => json is null ? 0 : Encoding.UTF8.GetByteCount(json);

    /// <summary>Reads a timestamp column as an offset-zero UTC value.</summary>
    /// <param name="reader">Current query row.</param>
    /// <param name="ordinal">Timestamp column ordinal.</param>
    /// <returns>The UTC timestamp.</returns>
    private DateTimeOffset ReadUtc(NpgsqlDataReader reader, int ordinal) => this.AsUtc(reader.GetDateTime(ordinal));

    /// <summary>Normalizes a PostgreSQL UTC timestamp without relying on machine local time.</summary>
    /// <param name="value">Database timestamp.</param>
    /// <returns>A zero-offset timestamp.</returns>
    private DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
