using Devbone.Frameworks.Ddd.Core;
using Devbone.Frameworks.Ddd.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SonosMonitoring.Api;
using SonosMonitoring.Application.Monitoring;
using SonosMonitoring.Domain.Monitoring.Disable;
using SonosMonitoring.Domain.Monitoring.Enable;
using SonosMonitoring.Integration.Tests.Infrastructure;

namespace SonosMonitoring.Integration.Tests.Monitoring;

/// <summary>Verifies the real PostgreSQL adapter's commit, deduplication and concurrency contracts.</summary>
public sealed class MonitoringPersistenceTests : IClassFixture<IntegrationInfrastructureFixture>
{
    private readonly IntegrationInfrastructureFixture _infrastructure;

    /// <summary>Receives independently provisioned test infrastructure.</summary>
    /// <param name="infrastructure">Fresh fixture-owned databases.</param>
    public MonitoringPersistenceTests(IntegrationInfrastructureFixture infrastructure) => this._infrastructure = infrastructure;

    /// <summary>A retry returns the first receipt, and rejection never adds an event or outbox row.</summary>
    [Fact]
    public async Task CommitIsAtomicAndRequestKeyUsesFirstCommittedResult()
    {
        var config = new Dictionary<string, string?>(this._infrastructure.CreateApiConfigurationOverrides()) { ["Backend:DispatchOutbox"] = "false" };
        await using var app = ApiHost.Build([], config);
        var executor = app.Services.GetRequiredService<MonitoringExecutor>();
        var key = "RINCON_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var id = MonitoringIdentity.ForDevice(key);
        var requestId = RequestId.New;
        var first = await executor.Handle([new EnableMonitoring(id, requestId, key, 15)]);
        Assert.True(first.Success, string.Join(", ", first.Errors.Select(error => error.Code)));
        var retry = await executor.Handle([new EnableMonitoring(id, requestId, key, 30)]);
        Assert.True(retry.Success);
        Assert.True(retry.CommitReceipt!.AlreadyCommitted);
        Assert.Equal(first.CommitReceipt!.AggregateConcurrencyToken, retry.CommitReceipt.AggregateConcurrencyToken);
        var conflict = await executor.Handle([new EnableMonitoring(id, RequestId.New, key, 15)]);
        Assert.False(conflict.Success);
        await using var connection = new NpgsqlConnection(this._infrastructure.EventStoreConnectionString);
        await connection.OpenAsync();
        await using var counts = new NpgsqlCommand("""
            SELECT
                (SELECT count(*) FROM events WHERE "AggregateId" = @id),
                (SELECT count(*) FROM event_outbox WHERE "AggregateId" = @id)
            """, connection);
        counts.Parameters.AddWithValue("id", id.ToString());
        await using var reader = await counts.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt64(0));
        Assert.Equal(1, reader.GetInt64(1));
    }

    /// <summary>Two independent actions with one token cannot both commit against the same head.</summary>
    [Fact]
    public async Task ConcurrentDeactivationsHaveExactlyOneWinner()
    {
        var config = new Dictionary<string, string?>(this._infrastructure.CreateApiConfigurationOverrides()) { ["Backend:DispatchOutbox"] = "false" };
        await using var app = ApiHost.Build([], config);
        var executor = app.Services.GetRequiredService<MonitoringExecutor>();
        var key = "RINCON_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var id = MonitoringIdentity.ForDevice(key);
        var first = await executor.Handle([new EnableMonitoring(id, RequestId.New, key, 15)]);
        Assert.True(first.Success);
        var token = first.CommitReceipt!.AggregateConcurrencyToken;
        var results = await Task.WhenAll(
            executor.Handle([new DisableMonitoring(id, RequestId.New, token)]),
            executor.Handle([new DisableMonitoring(id, RequestId.New, token)]));
        Assert.Single(results, result => result.Success);
        Assert.Single(results, result => result.Success == false);
    }
}
