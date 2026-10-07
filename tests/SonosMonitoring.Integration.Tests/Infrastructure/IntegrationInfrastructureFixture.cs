using System.Net.Sockets;
using Devbone.Frameworks.EventSourcing.Sql;
using MongoDB.Bson;
using MongoDB.Driver;
using Npgsql;

namespace SonosMonitoring.Integration.Tests.Infrastructure;

/// <summary>
/// Connects integration tests to the dedicated Compose services and provisions isolated databases.
/// </summary>
public sealed class IntegrationInfrastructureFixture : IAsyncLifetime
{
    private const string POSTGRES_HOST = "127.0.0.1";
    private const int POSTGRES_PORT = 48433;
    private const string POSTGRES_USER = "sonosmonitoring_test";
    private const string POSTGRES_PASSWORD = "sonosmonitoring_test_password";
    private const string POSTGRES_ADMIN_DATABASE = "sonosmonitoring_tests";
    private const string EVENT_TABLE_NAME = "events";
    private const string AGGREGATE_COMMIT_TABLE_NAME = "aggregate_commits";
    private const string AGGREGATE_HEAD_TABLE_NAME = "aggregate_heads";
    private const string INCREMENT_TABLE_NAME = "increments";
    private const string UNIQUE_TABLE_NAME = "uniques";
    private const string OUTBOX_TABLE_NAME = "event_outbox";
    private const string OUTBOX_NOTIFICATION_CHANNEL = "devbone_outbox";
    private const string RABBITMQ_HOST = "127.0.0.1";
    private const int RABBITMQ_PORT = 48673;
    private const string RABBITMQ_USER = "sonosmonitoring_test";
    private const string RABBITMQ_PASSWORD = "sonosmonitoring_test_password";
    private const string RABBITMQ_VIRTUAL_HOST = "/";
    private const string RABBITMQ_EXCHANGE_NAME = "sonosmonitoring.events";
    private const string MONGODB_HOST = "127.0.0.1";
    private const int MONGODB_PORT = 48018;
    private const string MONGODB_REPLICA_SET = "rs0";
    private const int SERVICE_TIMEOUT_SECONDS = 90;

    private readonly string _eventStoreDatabaseName;
    private readonly string _mongoDatabaseName;
    private readonly string _rebuildMongoDatabaseName;
    private readonly string _deploymentId;
    private readonly string _projectionQueuePrefix;
    private readonly string _internalAddressPrefix;
    private bool _eventStoreDatabaseCreated;

    /// <summary>
    /// Initializes a fixture with unique database names for the lifetime of this fixture instance.
    /// </summary>
    public IntegrationInfrastructureFixture()
    {
        string suffix = Guid.NewGuid().ToString("N");
        this._eventStoreDatabaseName = $"sonos_it_{suffix}";
        this._mongoDatabaseName = $"sonos_it_{suffix}";
        this._rebuildMongoDatabaseName = $"sonos_it_{suffix}_rebuild";
        this._deploymentId = $"sonosmonitoring-it-{suffix}";
        this._projectionQueuePrefix = $"sonos-it-{suffix}";
        this._internalAddressPrefix = $"sonosmonitoring.it.{suffix}";
    }

    /// <summary>
    /// Gets the isolated PostgreSQL database name created for this fixture.
    /// </summary>
    public string EventStoreDatabaseName => this._eventStoreDatabaseName;

    /// <summary>
    /// Gets the MongoDB database name assigned to this fixture.
    /// </summary>
    public string MongoDatabaseName => this._mongoDatabaseName;

    /// <summary>Gets the independent MongoDB database reserved for a full replay reconstruction.</summary>
    public string RebuildMongoDatabaseName => this._rebuildMongoDatabaseName;

    /// <summary>
    /// Gets the PostgreSQL connection string for the fixture database.
    /// </summary>
    public string EventStoreConnectionString =>
        IntegrationInfrastructureFixture.CreatePostgresConnectionString(this.EventStoreDatabaseName);

    /// <summary>
    /// Gets the RabbitMQ AMQP URL for the dedicated test broker.
    /// </summary>
    public string RabbitMqConnectionString =>
        $"amqp://{RABBITMQ_USER}:{RABBITMQ_PASSWORD}@{RABBITMQ_HOST}:{RABBITMQ_PORT}/%2f";

    /// <summary>
    /// Gets the MongoDB connection string for the one-node replica set through the mapped host port.
    /// </summary>
    public string MongoConnectionString =>
        $"mongodb://{MONGODB_HOST}:{MONGODB_PORT}/?replicaSet={MONGODB_REPLICA_SET}&directConnection=true";

    /// <summary>
    /// Gets the projection-worker configuration for the isolated test deployment.
    /// </summary>
    public IReadOnlyDictionary<string, string?> ProjectionConfigurationOverrides =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["EventProjection:Transport"] = "rabbitmq",
            ["EventProjection:Mongo:ConnectionString"] = this.MongoConnectionString,
            ["EventProjection:Mongo:DatabaseName"] = this.MongoDatabaseName,
            ["EventProjection:ControlPlane:DeploymentId"] = this._deploymentId,
            ["EventProjection:ControlPlane:Revision"] = "integration-test",
            ["EventProjection:InternalAddressPrefix"] = this._internalAddressPrefix,
            ["EventProjection:RabbitMq:HostName"] = RABBITMQ_HOST,
            ["EventProjection:RabbitMq:Port"] = RABBITMQ_PORT.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["EventProjection:RabbitMq:UserName"] = RABBITMQ_USER,
            ["EventProjection:RabbitMq:Password"] = RABBITMQ_PASSWORD,
            ["EventProjection:RabbitMq:VirtualHost"] = RABBITMQ_VIRTUAL_HOST,
            ["EventProjection:RabbitMq:DomainExchangeName"] = this._projectionQueuePrefix + ".events",
            ["EventProjection:RabbitMq:InternalExchangeName"] = this._projectionQueuePrefix + ".internal",
            ["EventProjection:RabbitMq:QueuePrefix"] = this._projectionQueuePrefix,
            ["EventProjection:RabbitMq:UseSingleActiveConsumer"] = "true",
        };

    /// <summary>Creates an isolated second projection deployment sharing only the source domain exchange.</summary>
    /// <returns>Worker configuration for reconstructing the complete retained stream.</returns>
    public IReadOnlyDictionary<string, string?> CreateRebuildProjectionConfigurationOverrides()
    {
        Dictionary<string, string?> values = this.ProjectionConfigurationOverrides.ToDictionary(
            entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        values["EventProjection:Mongo:DatabaseName"] = this.RebuildMongoDatabaseName;
        values["EventProjection:ControlPlane:DeploymentId"] = this._deploymentId + "-rebuild";
        values["EventProjection:InternalAddressPrefix"] = this._internalAddressPrefix + ".rebuild";
        values["EventProjection:RabbitMq:QueuePrefix"] = this._projectionQueuePrefix + "-rebuild";
        values["EventProjection:RabbitMq:InternalExchangeName"] = this._projectionQueuePrefix + ".rebuild.internal";
        return values;
    }

    /// <summary>
    /// Waits for all dedicated services and creates the fresh event-store database schema.
    /// </summary>
    /// <returns>A task that completes when the services and schema are ready.</returns>
    public async Task InitializeAsync()
    {
        try
        {
            await this.WaitForPostgresAsync();
            await this.WaitForRabbitMqAsync();
            await this.WaitForMongoReplicaSetAsync();
            await this.CreateEventStoreDatabaseAsync();
        }
        catch
        {
            await this.DropEventStoreDatabaseAsync();
            throw;
        }
    }

    /// <summary>
    /// Drops databases owned by this fixture after its tests finish.
    /// </summary>
    /// <returns>A task that completes after fixture-owned databases are removed.</returns>
    public async Task DisposeAsync()
    {
        await this.DropEventStoreDatabaseAsync();

        MongoClient mongoClient = this.CreateMongoClient();
        await mongoClient.DropDatabaseAsync(this.MongoDatabaseName);
        await mongoClient.DropDatabaseAsync(this.RebuildMongoDatabaseName);
    }

    /// <summary>
    /// Creates configuration values suitable for the API integration-test host.
    /// </summary>
    /// <returns>Configuration keys for EventStore and Backend settings.</returns>
    public IReadOnlyDictionary<string, string?> CreateApiConfigurationOverrides()
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CollectionSettings:Path"] = Path.Combine(Path.GetTempPath(), "sonos-isolated-" + Guid.NewGuid().ToString("N"), "settings.json"),
            ["Backend:Enabled"] = "true",
            ["EventStore:Host"] = POSTGRES_HOST,
            ["EventStore:Port"] = POSTGRES_PORT.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["EventStore:DatabaseName"] = this.EventStoreDatabaseName,
            ["EventStore:User"] = POSTGRES_USER,
            ["EventStore:Password"] = POSTGRES_PASSWORD,
            ["EventStore:EventTableName"] = EVENT_TABLE_NAME,
            ["EventStore:AggregateCommitTableName"] = AGGREGATE_COMMIT_TABLE_NAME,
            ["EventStore:AggregateHeadTableName"] = AGGREGATE_HEAD_TABLE_NAME,
            ["EventStore:IncrementGlobalAggrTableName"] = INCREMENT_TABLE_NAME,
            ["EventStore:UniqueTableName"] = UNIQUE_TABLE_NAME,
            ["EventStore:OutboxTableName"] = OUTBOX_TABLE_NAME,
            ["EventStore:OutboxNotificationChannel"] = OUTBOX_NOTIFICATION_CHANNEL,
            ["EventStore:RequireOutboxForEventWrites"] = "true",
            ["Backend:RabbitMq:HostName"] = RABBITMQ_HOST,
            ["Backend:RabbitMq:Port"] = RABBITMQ_PORT.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Backend:RabbitMq:UserName"] = RABBITMQ_USER,
            ["Backend:RabbitMq:Password"] = RABBITMQ_PASSWORD,
            ["Backend:RabbitMq:VirtualHost"] = RABBITMQ_VIRTUAL_HOST,
            ["Backend:RabbitMq:ExchangeName"] = this._projectionQueuePrefix + ".events",
            ["Backend:MongoConnectionString"] = this.MongoConnectionString,
            ["Backend:MongoDatabase"] = this.MongoDatabaseName,
        };
    }

    /// <summary>
    /// Waits until PostgreSQL accepts a connection to the bootstrap database.
    /// </summary>
    /// <returns>A task that completes when PostgreSQL is available.</returns>
    private async Task WaitForPostgresAsync()
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(SERVICE_TIMEOUT_SECONDS);
        Exception? lastException = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await using NpgsqlConnection connection = new(
                    IntegrationInfrastructureFixture.CreatePostgresConnectionString(POSTGRES_ADMIN_DATABASE));
                await connection.OpenAsync();
                return;
            }
            catch (NpgsqlException exception)
            {
                lastException = exception;
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException("PostgreSQL did not become available to the integration fixture.", lastException);
    }

    /// <summary>
    /// Waits until the dedicated RabbitMQ broker accepts TCP connections.
    /// </summary>
    /// <returns>A task that completes when RabbitMQ is available.</returns>
    private async Task WaitForRabbitMqAsync()
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(SERVICE_TIMEOUT_SECONDS);
        Exception? lastException = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using TcpClient client = new();
                await client.ConnectAsync(RABBITMQ_HOST, RABBITMQ_PORT).WaitAsync(TimeSpan.FromSeconds(2));
                return;
            }
            catch (Exception exception) when (exception is SocketException or TimeoutException)
            {
                lastException = exception;
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException("RabbitMQ did not become available to the integration fixture.", lastException);
    }

    /// <summary>
    /// Waits until MongoDB reports the primary state for replica set rs0.
    /// </summary>
    /// <returns>A task that completes when the MongoDB replica set has a primary.</returns>
    private async Task WaitForMongoReplicaSetAsync()
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(SERVICE_TIMEOUT_SECONDS);
        Exception? lastException = null;
        MongoClient client = this.CreateMongoClient();

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                BsonDocument hello = await client.GetDatabase("admin")
                    .RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1));
                if (hello.GetValue("isWritablePrimary", false).AsBoolean)
                {
                    return;
                }
            }
            catch (MongoException exception)
            {
                lastException = exception;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException("MongoDB replica set rs0 did not become primary.", lastException);
    }

    /// <summary>
    /// Creates a unique database and applies the framework's canonical schema to it.
    /// </summary>
    /// <returns>A task that completes after the schema has been initialized.</returns>
    private async Task CreateEventStoreDatabaseAsync()
    {
        await using NpgsqlConnection adminConnection = new(
            IntegrationInfrastructureFixture.CreatePostgresConnectionString(POSTGRES_ADMIN_DATABASE));
        await adminConnection.OpenAsync();

        await using (NpgsqlCommand createDatabase = new(
            $"CREATE DATABASE \"{this.EventStoreDatabaseName}\"",
            adminConnection))
        {
            await createDatabase.ExecuteNonQueryAsync();
            this._eventStoreDatabaseCreated = true;
        }

        await using NpgsqlConnection eventStoreConnection = new(this.EventStoreConnectionString);
        await eventStoreConnection.OpenAsync();
        await using NpgsqlCommand createSchema = new(EventStoreSchema.GetPostgresScript(), eventStoreConnection);
        await createSchema.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Drops the event-store database if this fixture created it.
    /// </summary>
    /// <returns>A task that completes after the event-store database is dropped.</returns>
    private async Task DropEventStoreDatabaseAsync()
    {
        if (this._eventStoreDatabaseCreated == false)
        {
            return;
        }

        await using NpgsqlConnection adminConnection = new(
            IntegrationInfrastructureFixture.CreatePostgresConnectionString(POSTGRES_ADMIN_DATABASE));
        await adminConnection.OpenAsync();
        await using NpgsqlCommand dropDatabase = new(
            $"DROP DATABASE IF EXISTS \"{this.EventStoreDatabaseName}\" WITH (FORCE)",
            adminConnection);
        await dropDatabase.ExecuteNonQueryAsync();
        this._eventStoreDatabaseCreated = false;
    }

    /// <summary>
    /// Creates a PostgreSQL connection string using the supplied database name.
    /// </summary>
    /// <param name="databaseName">The database to select.</param>
    /// <returns>A connection string for the dedicated test PostgreSQL service.</returns>
    private static string CreatePostgresConnectionString(string databaseName)
    {
        NpgsqlConnectionStringBuilder builder = new()
        {
            Host = POSTGRES_HOST,
            Port = POSTGRES_PORT,
            Database = databaseName,
            Username = POSTGRES_USER,
            Password = POSTGRES_PASSWORD,
            Timeout = 2,
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Creates a MongoDB client configured for direct access through the mapped host port.
    /// </summary>
    /// <returns>A client for the dedicated MongoDB replica set.</returns>
    private MongoClient CreateMongoClient()
    {
        MongoClientSettings settings = MongoClientSettings.FromConnectionString(this.MongoConnectionString);
        settings.ConnectTimeout = TimeSpan.FromSeconds(2);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
        return new MongoClient(settings);
    }
}
