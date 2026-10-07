using Devbone.Frameworks.EventSourcing.Repositories.Settings;
using Devbone.Frameworks.EventSourcing.Sql;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Npgsql;

namespace SonosMonitoring.Infrastructure.Monitoring;

/// <summary>Checks the dependencies needed to accept commands and read policies without testing Sonos reachability.</summary>
public sealed class BackendReadiness
{
    private readonly string _connectionString;
    private readonly IMongoDatabase _database;

    /// <summary>Uses the same database configuration as the write and read adapters.</summary>
    /// <param name="configuration">External persistence configuration.</param>
    /// <param name="database">Shared MongoDB database client.</param>
    public BackendReadiness(IConfiguration configuration, IMongoDatabase database)
    {
        var settings = configuration.GetSection("EventStore").Get<EventStoreSettings>()!;
        this._connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = settings.Host,
            Port = checked((int)settings.Port),
            Database = settings.DatabaseName,
            Username = settings.User,
            Password = settings.Password,
            Timeout = 2,
        }.ConnectionString;
        this._database = database;
    }

    /// <summary>Checks schema and read-store reachability within a fixed total budget.</summary>
    /// <param name="ct">HTTP request cancellation.</param>
    public async Task<bool> CheckAsync(CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await using var connection = new NpgsqlConnection(this._connectionString);
            await connection.OpenAsync(budget.Token);
            var version = await EventStoreSchema.ReadAppliedVersionAsync(connection, cancellationToken: budget.Token);
            await this._database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: budget.Token);
            return version == EventStoreSchema.SCHEMA_VERSION;
        }
        catch (Exception exception) when (exception is NpgsqlException or MongoException or OperationCanceledException)
        {
            return false;
        }
    }
}
