using Npgsql;
using SonosMonitoring.Application.History;
using SonosMonitoring.Infrastructure.History;
using SonosMonitoring.Integration.Tests.Infrastructure;

namespace SonosMonitoring.Integration.Tests.History;

/// <summary>Verifies the observation journal against the fixture-owned PostgreSQL database.</summary>
public sealed class ObservationHistoryTests : IClassFixture<IntegrationInfrastructureFixture>, IAsyncLifetime
{
    private readonly IntegrationInfrastructureFixture _infrastructure;

    /// <summary>Receives an isolated PostgreSQL database and creates the explicit history schema once.</summary>
    /// <param name="infrastructure">Fixture-owned disposable database.</param>
    public ObservationHistoryTests(IntegrationInfrastructureFixture infrastructure) => this._infrastructure = infrastructure;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await using NpgsqlConnection connection = new(this._infrastructure.EventStoreConnectionString);
        await connection.OpenAsync();
        await using (NpgsqlCommand dropSchema = new("DROP SCHEMA IF EXISTS sonos_history CASCADE", connection))
        {
            await dropSchema.ExecuteNonQueryAsync();
        }
        await using NpgsqlCommand command = new(ObservationHistorySchema.GetPostgresScript(), connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Writes baselines, suppresses unchanged polling samples, and records only a real state change.</summary>
    [Fact]
    public async Task WritesBaselineAndChangedStateWithoutPollingDuplicates()
    {
        await this.ResetAsync();
        PostgresObservationHistory firstStore = this.CreateStore();
        Guid runId = Guid.NewGuid();
        DateTimeOffset capturedAt = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        ObservedDevice first = this.CreateDevice("RINCON_HISTORY_ONE", "192.0.2.10");
        await firstStore.WriteAsync(runId, capturedAt, true, [this.CreateSample(first)], false, CancellationToken.None);
        await firstStore.WriteAsync(runId, capturedAt.AddSeconds(2), true, [this.CreateSample(first)], false, CancellationToken.None);

        PostgresObservationHistory restartedStore = this.CreateStore();
        Guid restartedRunId = Guid.NewGuid();
        await restartedStore.WriteAsync(restartedRunId, capturedAt.AddSeconds(3), true, [this.CreateSample(first)], false, CancellationToken.None);
        ObservationPage baseline = await restartedStore.ReadAsync(0, 200, first.DeviceId, CancellationToken.None);
        Assert.Equal(new[] { "gap", "baseline", "gap" }, baseline.Changes.Select(change => change.Kind));
        ObservationChange baselineChange = Assert.Single(baseline.Changes, change => change.Kind == "baseline");
        ObservedDevice baselineState = baselineChange.After ?? throw new InvalidOperationException("Baseline state is missing.");
        Assert.Null(baselineChange.Before);
        Assert.Equal(first.DeviceId, baselineState.DeviceId);
        Assert.Equal(first.Address, baselineState.Address);
        Assert.Equal(first.Groups[0].GroupId, baselineState.Groups[0].GroupId);
        Assert.Equal(capturedAt.AddSeconds(3), baseline.LastRecordedAtUtc);

        ObservedDevice changed = this.CreateDevice(first.DeviceId, "192.0.2.11", topologyState: "stale");
        await restartedStore.WriteAsync(restartedRunId, capturedAt.AddSeconds(4), true, [this.CreateSample(changed)], false, CancellationToken.None);
        ObservationPage changes = await restartedStore.ReadAsync(baseline.NextCursor, 200, first.DeviceId, CancellationToken.None);
        ObservationChange change = Assert.Single(changes.Changes);
        ObservedDevice before = change.Before ?? throw new InvalidOperationException("Changed state has no before value.");
        ObservedDevice after = change.After ?? throw new InvalidOperationException("Changed state has no after value.");
        Assert.Equal("change", change.Kind);
        Assert.Equal(first.Address, before.Address);
        Assert.Equal(changed.Address, after.Address);
        Assert.Equal(changed.TopologyState, after.TopologyState);
    }

    /// <summary>Records run restart, storage failure, collection outage, and resumption as global gaps.</summary>
    [Fact]
    public async Task WritesCoverageGapsAndRetainsThemForDeviceFilteredReads()
    {
        await this.ResetAsync();
        PostgresObservationHistory store = this.CreateStore();
        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        Guid firstRun = Guid.NewGuid();
        Guid nextRun = Guid.NewGuid();
        await store.WriteAsync(firstRun, capturedAt, true, [], false, CancellationToken.None);
        await store.WriteAsync(nextRun, capturedAt.AddSeconds(1), true, [], true, CancellationToken.None);
        await store.WriteAsync(nextRun, capturedAt.AddSeconds(2), false, [], false, CancellationToken.None);
        await store.WriteAsync(nextRun, capturedAt.AddSeconds(3), true, [], false, CancellationToken.None);

        ObservationPage page = await store.ReadAsync(0, 200, "RINCON_NOT_PRESENT", CancellationToken.None);

        Assert.Equal(new[] { "collector_restart", "collector_restart", "storage_unavailable", "collector_unavailable", "collector_resumed" },
            page.Changes.Select(change => change.Reason));
        Assert.All(page.Changes, change =>
        {
            Assert.Equal("gap", change.Kind);
            Assert.Null(change.Before);
            Assert.Null(change.After);
        });
    }

    /// <summary>Paginates in global sequence order and applies an exact device filter without hiding global gaps.</summary>
    [Fact]
    public async Task ReadsOrderedPagesAndKeepsGlobalGapsUnderFiltering()
    {
        await this.ResetAsync();
        PostgresObservationHistory store = this.CreateStore();
        Guid runId = Guid.NewGuid();
        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        ObservedDevice[] devices =
        [
            this.CreateDevice("RINCON_PAGE_ONE", "192.0.2.21"),
            this.CreateDevice("RINCON_PAGE_TWO", "192.0.2.22"),
            this.CreateDevice("RINCON_PAGE_THREE", "192.0.2.23")
        ];
        await store.WriteAsync(runId, capturedAt, true, devices.Select(this.CreateSample).ToArray(), false, CancellationToken.None);

        ObservationPage first = await store.ReadAsync(0, 2, null, CancellationToken.None);
        ObservationPage second = await store.ReadAsync(first.NextCursor, 2, null, CancellationToken.None);
        ObservationPage filtered = await store.ReadAsync(0, 200, devices[1].DeviceId, CancellationToken.None);

        Assert.Equal(new[] { "gap", "baseline" }, first.Changes.Select(change => change.Kind));
        Assert.Equal(2, first.Changes.Count);
        Assert.Equal(2, first.NextCursor);
        Assert.Equal(new[] { "baseline", "baseline" }, second.Changes.Select(change => change.Kind));
        Assert.Equal(2, second.Changes.Count);
        Assert.Equal(4L, second.Changes[1].Sequence);
        Assert.Equal(1L, first.EarliestCursor);
        Assert.Equal(devices[1].DeviceId, Assert.Single(filtered.Changes, change => change.Kind == "baseline").After!.DeviceId);

        await store.WriteAsync(runId, capturedAt.AddSeconds(1), false, [], false, CancellationToken.None);
        ObservationPage filteredWithGap = await store.ReadAsync(0, 200, devices[1].DeviceId, CancellationToken.None);
        Assert.Equal(new[] { "gap", "baseline", "gap" }, filteredWithGap.Changes.Select(change => change.Kind));
    }

    /// <summary>Applies both age and row-count retention while preserving the newest bounded entries.</summary>
    [Fact]
    public async Task RetentionEnforcesAgeAndMaximumRows()
    {
        await this.ResetAsync();
        PostgresObservationHistory store = this.CreateStore(retentionDays: 1, maxEntries: 3);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ObservedDevice[] oldDevices =
        [
            this.CreateDevice("RINCON_OLD_ONE", "192.0.2.31"),
            this.CreateDevice("RINCON_OLD_TWO", "192.0.2.32")
        ];
        await store.WriteAsync(Guid.NewGuid(), now.AddDays(-3), true, oldDevices.Select(this.CreateSample).ToArray(), false, CancellationToken.None);
        ObservedDevice[] newDevices = Enumerable.Range(1, 5)
            .Select(index => this.CreateDevice("RINCON_NEW_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "192.0.2." + (40 + index).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        Guid retentionRun = Guid.NewGuid();
        await store.WriteAsync(retentionRun, now.AddSeconds(-1), true, newDevices.Select(this.CreateSample).ToArray(), false, CancellationToken.None);

        ObservationPage page = await store.ReadAsync(0, 200, null, CancellationToken.None);

        Assert.Equal(3, page.Changes.Count);
        Assert.Equal(page.Changes[0].Sequence, page.EarliestCursor);
        Assert.All(page.Changes, change => Assert.True(change.RecordedAtUtc >= now.AddDays(-1)));
        Assert.Equal(newDevices.Skip(2).Select(device => device.DeviceId), page.Changes.Select(change => change.After!.DeviceId));
        Assert.Equal(1, page.RetentionDays);
        Assert.Equal(3, page.MaxEntries);
    }

    /// <summary>Skips row-count scans on unchanged writes while applying a changed cap on a new store's first write.</summary>
    [Fact]
    public async Task UnchangedWritesSkipCountRetentionAndNewStoreAppliesChangedCap()
    {
        await this.ResetAsync();
        PostgresObservationHistory store = this.CreateStore(maxEntries: 100000);
        Guid runId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ObservedDevice device = this.CreateDevice("RINCON_RETENTION_UNCHANGED", "192.0.2.71");
        await store.WriteAsync(runId, now, true, [this.CreateSample(device)], false, CancellationToken.None);

        await using (NpgsqlConnection connection = new(this._infrastructure.EventStoreConnectionString))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand seed = new("""
                INSERT INTO sonos_history.entries (kind, recorded_at, reason)
                SELECT 'gap', @recorded_at, 'collector_unavailable'
                FROM generate_series(1, 100000)
                """, connection);
            seed.Parameters.AddWithValue("recorded_at", now);
            await seed.ExecuteNonQueryAsync();
        }

        await store.WriteAsync(runId, now.AddSeconds(1), true, [this.CreateSample(device)], false, CancellationToken.None);
        Assert.Equal(100002, await this.ReadEntryCountAsync());

        PostgresObservationHistory reducedCapStore = this.CreateStore(maxEntries: 100000);
        await reducedCapStore.WriteAsync(runId, now.AddSeconds(2), true, [this.CreateSample(device)], false, CancellationToken.None);
        Assert.Equal(100000, await this.ReadEntryCountAsync());
    }

    /// <summary>Fails closed when the installed journal schema version is unsupported.</summary>
    [Fact]
    public async Task VerifyAsyncRejectsUnsupportedSchemaVersion()
    {
        await this.ResetAsync();
        await using NpgsqlConnection connection = new(this._infrastructure.EventStoreConnectionString);
        await connection.OpenAsync();
        await using (NpgsqlCommand setVersion = new("UPDATE sonos_history.schema_version SET version = 99 WHERE singleton = true", connection))
        {
            await setVersion.ExecuteNonQueryAsync();
        }
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => this.CreateStore().VerifyAsync(CancellationToken.None));
        }
        finally
        {
            await using NpgsqlCommand restore = new("UPDATE sonos_history.schema_version SET version = 1 WHERE singleton = true", connection);
            await restore.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Clears only the dedicated test schema before one scenario.</summary>
    /// <returns>A task that completes after the fixture-owned journal tables are reset.</returns>
    private async Task ResetAsync()
    {
        await using NpgsqlConnection connection = new(this._infrastructure.EventStoreConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new("""
            TRUNCATE sonos_history.entries RESTART IDENTITY;
            TRUNCATE sonos_history.checkpoints;
            UPDATE sonos_history.meta
            SET run_id = NULL, last_recorded = NULL, collector_observing = NULL
            WHERE singleton = true;
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Reads the row count from the fixture-owned history schema.</summary>
    /// <returns>The number of retained entries.</returns>
    private async Task<long> ReadEntryCountAsync()
    {
        await using NpgsqlConnection connection = new(this._infrastructure.EventStoreConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new("SELECT COUNT(*) FROM sonos_history.entries", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Creates a store against this test class's unique database.</summary>
    /// <param name="retentionDays">Optional age retention override.</param>
    /// <param name="maxEntries">Optional maximum entry count override.</param>
    /// <returns>A new independent store instance.</returns>
    private PostgresObservationHistory CreateStore(int retentionDays = 90, int maxEntries = 20000) =>
        new(new ObservationHistoryOptions
        {
            Enabled = true,
            ConnectionString = this._infrastructure.EventStoreConnectionString,
            RetentionDays = retentionDays,
            MaxEntries = maxEntries
        });

    /// <summary>Creates a compact normalized device state with one stable grouping reference.</summary>
    /// <param name="deviceId">Canonical Sonos identity.</param>
    /// <param name="address">Validated source address.</param>
    /// <param name="topologyState">Normalized topology quality.</param>
    /// <returns>Comparable observation state.</returns>
    private ObservedDevice CreateDevice(string deviceId, string address, string topologyState = "available") =>
        new(deviceId, address, "available", topologyState,
            [new("GROUP_1", deviceId, [new(deviceId, null)])]);

    /// <summary>Wraps one state with a source observation timestamp.</summary>
    /// <param name="device">Normalized source state.</param>
    /// <returns>A sample input for the journal.</returns>
    private ObservationSample CreateSample(ObservedDevice device) => new(device, DateTimeOffset.UtcNow);
}
