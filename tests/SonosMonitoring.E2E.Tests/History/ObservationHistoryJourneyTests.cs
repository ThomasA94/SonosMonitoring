using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SonosMonitoring.Api;
using SonosMonitoring.Contracts.History;
using SonosMonitoring.Infrastructure.Collection;
using SonosMonitoring.Infrastructure.History;
using SonosMonitoring.Integration.Tests.Infrastructure;

namespace SonosMonitoring.E2E.Tests.History;

/// <summary>Checks protected API history through actual PostgreSQL and a process-composition restart.</summary>
public sealed class ObservationHistoryJourneyTests : IClassFixture<IntegrationInfrastructureFixture>
{
    private const string KEY = "history-test-key-01234567890123456789";
    private readonly IntegrationInfrastructureFixture _infrastructure;

    /// <summary>Uses a fresh disposable database independent of local development storage.</summary>
    /// <param name="infrastructure">Fixture-owned dependencies.</param>
    public ObservationHistoryJourneyTests(IntegrationInfrastructureFixture infrastructure) => this._infrastructure = infrastructure;

    /// <summary>Observed changes survive restart, while access, paging bounds, and coverage boundaries remain visible.</summary>
    [Fact]
    public async Task HistorySurvivesRestartAndRemainsProtected()
    {
        await using (var connection = new NpgsqlConnection(this._infrastructure.EventStoreConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(ObservationHistorySchema.GetPostgresScript(), connection);
            await command.ExecuteNonQueryAsync();
        }
        Dictionary<string, string?> config = new()
        {
            ["CollectionSettings:Path"] = Path.Combine(Path.GetTempPath(), "sonos-isolated-" + Guid.NewGuid().ToString("N"), "settings.json"),
            ["Backend:Enabled"] = "false",
            ["Collector:Enabled"] = "false",
            ["Access:ApiKey"] = KEY,
            ["History:Enabled"] = "true",
            ["History:ConnectionString"] = this._infrastructure.EventStoreConnectionString,
            ["History:SampleInterval"] = "00:00:01"
        };
        await using var api = ApiHost.Build([], config);
        var state = api.Services.GetRequiredService<CollectorState>();
        state.DescriptionSucceeded(new("RINCON_HISTORY", "Room", "Model"), "192.0.2.1", DateTimeOffset.UtcNow);
        api.Urls.Clear();
        api.Urls.Add("http://127.0.0.1:0");
        await api.StartAsync();
        using var client = new HttpClient { BaseAddress = new(api.Urls.Single()) };
        using var denied = await client.GetAsync("/api/collection/history");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        client.DefaultRequestHeaders.Add("X-Api-Key", KEY);
        var baseline = await WaitForAsync(client, page => page.Changes.Any(change => change.Kind == "baseline"));
        Assert.Contains(baseline.Changes, change => change.Kind == "gap");
        state.DescriptionFailed("192.0.2.1", DateTimeOffset.UtcNow, "timeout");
        var changed = await WaitForAsync(client, page => page.Changes.Any(change => change.After?.DescriptionState == "failed"));
        Assert.Contains(changed.Changes, change => change.Before?.DescriptionState == "available" && change.After?.DescriptionState == "failed");
        using var invalid = await client.GetAsync("/api/collection/history?limit=201");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var filtered = (await client.GetFromJsonAsync<ObservationPageDto>("/api/collection/history?deviceId=RINCON_OTHER"))!;
        Assert.All(filtered.Changes, change => Assert.Equal("gap", change.Kind));
        await api.StopAsync();
        await using var restarted = ApiHost.Build([], config);
        restarted.Urls.Clear();
        restarted.Urls.Add("http://127.0.0.1:0");
        await restarted.StartAsync();
        using var afterRestart = new HttpClient { BaseAddress = new(restarted.Urls.Single()) };
        afterRestart.DefaultRequestHeaders.Add("X-Api-Key", KEY);
        var restored = await WaitForAsync(afterRestart, page => page.Changes.Count(change => change.Reason == "collector_restart") >= 2);
        Assert.Contains(restored.Changes, change => change.After?.DescriptionState == "failed");
        Assert.Equal(90, restored.RetentionDays);
        Assert.Equal(20000, restored.MaxEntries);
        await restarted.StopAsync();
    }

    /// <summary>Waits for the hosted journal, without replacing a failure by a skipped assertion.</summary>
    /// <param name="client">Authorized live API client.</param>
    /// <param name="predicate">Required durable observation.</param>
    private static async Task<ObservationPageDto> WaitForAsync(HttpClient client, Func<ObservationPageDto, bool> predicate)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var page = (await client.GetFromJsonAsync<ObservationPageDto>("/api/collection/history", budget.Token))!;
            if (predicate(page))
            { return page; }
            await Task.Delay(100, budget.Token);
        }
    }
}
