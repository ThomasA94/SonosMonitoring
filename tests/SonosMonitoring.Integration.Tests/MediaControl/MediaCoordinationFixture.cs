using Npgsql;
using SonosMonitoring.Infrastructure.MediaControl;
using SonosMonitoring.Integration.Tests.Infrastructure;

namespace SonosMonitoring.Integration.Tests.MediaControl;

/// <summary>Provisions a disposable database with the explicit media coordination schema.</summary>
public sealed class MediaCoordinationFixture : IAsyncLifetime
{
    private readonly IntegrationInfrastructureFixture _infrastructure = new();

    /// <summary>Gets the connection string for this fixture's isolated PostgreSQL database.</summary>
    public string ConnectionString => this._infrastructure.EventStoreConnectionString;

    /// <summary>Gets the isolated source projection configuration.</summary>
    public IReadOnlyDictionary<string, string?> ProjectionConfigurationOverrides => this._infrastructure.ProjectionConfigurationOverrides;

    /// <summary>Gets the isolated MongoDB connection string.</summary>
    public string MongoConnectionString => this._infrastructure.MongoConnectionString;

    /// <summary>Gets the independent rebuild MongoDB database.</summary>
    public string RebuildMongoDatabaseName => this._infrastructure.RebuildMongoDatabaseName;

    /// <summary>Creates an independent replay projection deployment.</summary>
    public IReadOnlyDictionary<string, string?> CreateRebuildProjectionConfigurationOverrides() =>
        this._infrastructure.CreateRebuildProjectionConfigurationOverrides();

    /// <summary>Creates API host configuration with the media slice enabled for this fixture database.</summary>
    public IReadOnlyDictionary<string, string?> CreateMediaApiConfigurationOverrides()
    {
        Dictionary<string, string?> config = new(this._infrastructure.CreateApiConfigurationOverrides(), StringComparer.OrdinalIgnoreCase)
        {
            ["MediaControl:Enabled"] = "true",
            ["MediaControl:ConnectionString"] = this.ConnectionString,
            ["MediaControl:LifetimeSeconds"] = "30",
            ["MediaControl:ExecutionSeconds"] = "10",
        };
        return config;
    }

    /// <summary>Creates the framework schema and then the separately versioned media schema.</summary>
    public async Task InitializeAsync()
    {
        await this._infrastructure.InitializeAsync();
        await using NpgsqlConnection connection = new(this.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand create = new(MediaCoordinationSchema.GetPostgresScript(), connection);
        await create.ExecuteNonQueryAsync();
    }

    /// <summary>Removes only databases owned by this fixture.</summary>
    public Task DisposeAsync() => this._infrastructure.DisposeAsync();
}
