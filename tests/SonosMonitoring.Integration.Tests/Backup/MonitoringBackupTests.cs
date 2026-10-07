using Devbone.Frameworks.Ddd.Core;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SonosMonitoring.Application.History;
using SonosMonitoring.Api;
using SonosMonitoring.Application.Monitoring;
using SonosMonitoring.Domain.Monitoring.Disable;
using SonosMonitoring.Domain.Monitoring.Enable;
using SonosMonitoring.Integration.Tests.Infrastructure;
using SonosMonitoring.Infrastructure.History;

namespace SonosMonitoring.Integration.Tests.Backup;

/// <summary>Proves framework-level retry behavior survives the repository's PostgreSQL backup and restore scripts.</summary>
public sealed class MonitoringBackupTests : IClassFixture<IntegrationInfrastructureFixture>
{
    private const string POSTGRES_USER = "sonosmonitoring_test";
    private const string POSTGRES_ADMIN_DATABASE = "sonosmonitoring_tests";
    private static readonly TimeSpan _processTimeout = TimeSpan.FromSeconds(120);
    private readonly IntegrationInfrastructureFixture _infrastructure;

    /// <summary>Receives the existing integration fixture and its isolated source database.</summary>
    /// <param name="infrastructure">The fixture-owned test database and service connection settings.</param>
    public MonitoringBackupTests(IntegrationInfrastructureFixture infrastructure) => this._infrastructure = infrastructure;

    /// <summary>Restores a committed command, verifies deduplication, then continues its aggregate history.</summary>
    [Fact]
    public async Task BackupRestorePreservesEventStoreRetryAndConcurrencyState()
    {
        string repositoryRoot = FindRepositoryRoot();
        string backupPath = Path.Combine(Path.GetTempPath(), $"sonosmonitoring-backup-{Guid.NewGuid():N}.dump");
        string restoreDatabaseName = this._infrastructure.EventStoreDatabaseName + "_restore";
        bool ownsBackupPath = false;
        bool restoreDatabaseMayExist = false;

        try
        {
            string deviceKey = "RINCON_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
            var aggregateId = MonitoringIdentity.ForDevice(deviceKey);
            RequestId requestId = RequestId.New;
            var sourceConfiguration = new Dictionary<string, string?>(this._infrastructure.CreateApiConfigurationOverrides())
            {
                ["Backend:DispatchOutbox"] = "false"
            };

            AggregateConcurrencyToken? confirmedToken = default;
            long confirmedEventSequence = 0;
            string observationDeviceId = "RINCON_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
            DateTimeOffset observationTime = DateTimeOffset.UtcNow;
            ObservationSample observationSample = new(
                new ObservedDevice(observationDeviceId, "192.0.2.10", "available", "unobserved", []), observationTime);
            await using (var sourceApi = ApiHost.Build([], sourceConfiguration))
            {
                MonitoringExecutor executor = sourceApi.Services.GetRequiredService<MonitoringExecutor>();
                var result = await executor.Handle([new EnableMonitoring(aggregateId, requestId, deviceKey, 15)]);
                Assert.True(result.Success, string.Join(", ", result.Errors.Select(error => error.Code)));
                confirmedToken = result.CommitReceipt!.AggregateConcurrencyToken;
                confirmedEventSequence = result.CommitReceipt.EventSequence;
            }

            (long sourceEvents, long sourceOutbox) = await ReadCountsAsync(
                this._infrastructure.EventStoreConnectionString, aggregateId.ToString());
            Assert.Equal(1, sourceEvents);
            Assert.Equal(1, sourceOutbox);

            await using (NpgsqlConnection sourceHistoryConnection = new(this._infrastructure.EventStoreConnectionString))
            {
                await sourceHistoryConnection.OpenAsync();
                await using NpgsqlCommand createHistorySchema = new(ObservationHistorySchema.GetPostgresScript(), sourceHistoryConnection);
                await createHistorySchema.ExecuteNonQueryAsync();
            }
            PostgresObservationHistory sourceHistory = this.CreateHistoryStore(this._infrastructure.EventStoreConnectionString);
            await sourceHistory.WriteAsync(Guid.NewGuid(), observationTime, true, [observationSample], false, CancellationToken.None);

            string containerId = (await BackupProcessRunner.RunCheckedAsync("docker",
            [
                "compose", "--project-name", "sonosmonitoring-tests", "--file",
                Path.Combine(repositoryRoot, "infra", "compose.tests.yaml"), "ps", "-q", "postgres"
            ], repositoryRoot, TimeSpan.FromSeconds(30))).Trim();
            Assert.False(string.IsNullOrWhiteSpace(containerId));

            Assert.False(File.Exists(backupPath));
            ownsBackupPath = true;
            await BackupProcessRunner.RunCheckedAsync("pwsh",
            [
                "-NoProfile", "-File", Path.Combine(repositoryRoot, "scripts", "backup-postgres.ps1"),
                "-ContainerName", containerId, "-Database", this._infrastructure.EventStoreDatabaseName,
                "-User", POSTGRES_USER, "-OutputPath", backupPath
            ], repositoryRoot, _processTimeout);

            string adminConnectionString = this.ConnectionStringFor(POSTGRES_ADMIN_DATABASE);
            Assert.False(await DatabaseExistsAsync(adminConnectionString, restoreDatabaseName));
            restoreDatabaseMayExist = true;
            await BackupProcessRunner.RunCheckedAsync("pwsh",
            [
                "-NoProfile", "-File", Path.Combine(repositoryRoot, "scripts", "restore-postgres.ps1"),
                "-ContainerName", containerId, "-User", POSTGRES_USER,
                "-Database", restoreDatabaseName, "-BackupPath", backupPath
            ], repositoryRoot, _processTimeout);

            string restoredConnectionString = this.ConnectionStringFor(restoreDatabaseName);
            (long restoredEvents, long restoredOutbox) = await ReadCountsAsync(restoredConnectionString, aggregateId.ToString());
            Assert.Equal(1, restoredEvents);
            Assert.Equal(1, restoredOutbox);

            PostgresObservationHistory restoredHistory = this.CreateHistoryStore(restoredConnectionString);
            await restoredHistory.VerifyAsync(CancellationToken.None);
            ObservationPage restoredHistoryPage = await restoredHistory.ReadAsync(0, 200, observationDeviceId, CancellationToken.None);
            ObservationChange restoredBaseline = Assert.Single(restoredHistoryPage.Changes,
                change => change.Kind == "baseline");
            ObservedDevice restoredState = restoredBaseline.After
                ?? throw new InvalidOperationException("Restored observation baseline is missing.");
            Assert.Null(restoredBaseline.Before);
            Assert.Equal(observationDeviceId, restoredState.DeviceId);
            Assert.Equal("192.0.2.10", restoredState.Address);
            Assert.Contains(restoredHistoryPage.Changes, change => change.Kind == "gap");

            await restoredHistory.WriteAsync(Guid.NewGuid(), observationTime.AddSeconds(2), true,
                [observationSample], false, CancellationToken.None);
            ObservationPage restartedHistoryPage = await restoredHistory.ReadAsync(0, 200, observationDeviceId, CancellationToken.None);
            Assert.Single(restartedHistoryPage.Changes, change => change.Kind == "baseline");
            Assert.Contains(restartedHistoryPage.Changes,
                change => change.Kind == "gap" && change.Reason == "collector_restart");

            Dictionary<string, string?> restoredConfiguration = new(sourceConfiguration, StringComparer.OrdinalIgnoreCase)
            {
                ["EventStore:DatabaseName"] = restoreDatabaseName
            };
            await using (var restoredApi = ApiHost.Build([], restoredConfiguration))
            {
                MonitoringExecutor executor = restoredApi.Services.GetRequiredService<MonitoringExecutor>();
                var retry = await executor.Handle([new EnableMonitoring(aggregateId, requestId, deviceKey, 30)]);
                Assert.True(retry.Success);
                Assert.True(retry.CommitReceipt!.AlreadyCommitted);
                Assert.Equal(confirmedToken, retry.CommitReceipt.AggregateConcurrencyToken);
                Assert.Equal(confirmedEventSequence, retry.CommitReceipt.EventSequence);
                (long retryEvents, long retryOutbox) = await ReadCountsAsync(restoredConnectionString, aggregateId.ToString());
                Assert.Equal(1, retryEvents);
                Assert.Equal(1, retryOutbox);

                var disabled = await executor.Handle(
                    [new DisableMonitoring(aggregateId, RequestId.New, confirmedToken)]);
                Assert.True(disabled.Success, string.Join(", ", disabled.Errors.Select(error => error.Code)));
                Assert.Equal(2, disabled.CommitReceipt!.EventSequence);
            }

            (long finalRestoredEvents, long finalRestoredOutbox) = await ReadCountsAsync(
                restoredConnectionString, aggregateId.ToString());
            Assert.Equal(2, finalRestoredEvents);
            Assert.Equal(2, finalRestoredOutbox);
            (long finalSourceEvents, long finalSourceOutbox) = await ReadCountsAsync(
                this._infrastructure.EventStoreConnectionString, aggregateId.ToString());
            Assert.Equal(1, finalSourceEvents);
            Assert.Equal(1, finalSourceOutbox);
        }
        finally
        {
            try
            {
                if (ownsBackupPath && File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
            finally
            {
                if (restoreDatabaseMayExist)
                {
                    await DropRestoreDatabaseIfPresentAsync(this.ConnectionStringFor(POSTGRES_ADMIN_DATABASE), restoreDatabaseName);
                }
            }
        }
    }

    /// <summary>Finds the repository root by walking upward from the test execution directory.</summary>
    /// <returns>The directory containing the solution and backup scripts.</returns>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SonosMonitoring.slnx")) &&
                File.Exists(Path.Combine(current.FullName, "scripts", "backup-postgres.ps1")) &&
                File.Exists(Path.Combine(current.FullName, "scripts", "restore-postgres.ps1")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root containing the solution and backup scripts.");
    }

    /// <summary>Builds a connection string for another database on the fixture's dedicated server.</summary>
    /// <param name="databaseName">Database to select.</param>
    /// <returns>A connection string using the fixture role and endpoint.</returns>
    private string ConnectionStringFor(string databaseName)
    {
        NpgsqlConnectionStringBuilder builder = new(this._infrastructure.EventStoreConnectionString)
        {
            Database = databaseName
        };
        return builder.ConnectionString;
    }

    /// <summary>Creates an observation-journal adapter connected to the selected isolated database.</summary>
    /// <param name="connectionString">Connection string for the fixture or restored test database.</param>
    /// <returns>A PostgreSQL observation-history store.</returns>
    private PostgresObservationHistory CreateHistoryStore(string connectionString) =>
        new(new ObservationHistoryOptions { ConnectionString = connectionString });

    /// <summary>Counts aggregate events and durable outbox rows for one stream.</summary>
    /// <param name="connectionString">Connection to the isolated event store.</param>
    /// <param name="aggregateId">Aggregate identifier to count.</param>
    /// <returns>Event and outbox row counts.</returns>
    private static async Task<(long Events, long Outbox)> ReadCountsAsync(string connectionString, string aggregateId)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new("""
            SELECT
                (SELECT count(*) FROM events WHERE "AggregateId" = @id),
                (SELECT count(*) FROM event_outbox WHERE "AggregateId" = @id)
            """, connection);
        command.Parameters.AddWithValue("id", aggregateId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    /// <summary>Checks whether a database name exists through the fixture admin database.</summary>
    /// <param name="adminConnectionString">Connection to the dedicated test admin database.</param>
    /// <param name="databaseName">Database name to look up.</param>
    /// <returns>Whether the database exists.</returns>
    private static async Task<bool> DatabaseExistsAsync(string adminConnectionString, string databaseName)
    {
        await using NpgsqlConnection connection = new(adminConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new("SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)", connection);
        command.Parameters.AddWithValue("name", databaseName);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Drops only the fresh restore database owned by this test after clearing its matching pool.</summary>
    /// <param name="adminConnectionString">Connection to the dedicated test admin database.</param>
    /// <param name="databaseName">Unique restore database created by this test.</param>
    private static async Task DropRestoreDatabaseIfPresentAsync(string adminConnectionString, string databaseName)
    {
        if (await DatabaseExistsAsync(adminConnectionString, databaseName) == false)
        {
            return;
        }

        NpgsqlConnectionStringBuilder targetBuilder = new(adminConnectionString) { Database = databaseName };
        using NpgsqlConnection poolKey = new(targetBuilder.ConnectionString);
        NpgsqlConnection.ClearPool(poolKey);

        await using NpgsqlConnection adminConnection = new(adminConnectionString);
        await adminConnection.OpenAsync();
        await using NpgsqlCommand dropDatabase = new(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", adminConnection);
        await dropDatabase.ExecuteNonQueryAsync();
    }
}
