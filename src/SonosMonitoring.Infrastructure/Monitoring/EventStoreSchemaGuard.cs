using Devbone.Frameworks.EventSourcing.Repositories.Settings;
using Devbone.Frameworks.EventSourcing.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace SonosMonitoring.Infrastructure.Monitoring;

/// <summary>Refuses writes against missing or incompatible schemas without changing a database at startup.</summary>
public sealed class EventStoreSchemaGuard : IHostedService
{
    private readonly string _connectionString;

    /// <summary>Constructs the database connection from the same settings as the framework.</summary>
    /// <param name="configuration">The host configuration.</param>
    public EventStoreSchemaGuard(IConfiguration configuration)
    {
        var settings = configuration.GetSection("EventStore").Get<EventStoreSettings>()!;
        this._connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = settings.Host,
            Port = checked((int)settings.Port),
            Database = settings.DatabaseName,
            Username = settings.User,
            Password = settings.Password,
            Timeout = 5,
        }.ConnectionString;
    }

    /// <summary>Checks the version before the host accepts requests.</summary>
    /// <param name="cancellationToken">Startup cancellation.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(this._connectionString);
        await connection.OpenAsync(cancellationToken);
        var version = await EventStoreSchema.ReadAppliedVersionAsync(connection, cancellationToken: cancellationToken);
        if (version != EventStoreSchema.SCHEMA_VERSION)
        {
            throw new InvalidOperationException($"Event store schema {version?.ToString() ?? "missing"} does not match {EventStoreSchema.SCHEMA_VERSION}. Initialize or migrate explicitly.");
        }
    }

    /// <summary>Has no background work to drain.</summary>
    /// <param name="cancellationToken">Shutdown cancellation.</param>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
