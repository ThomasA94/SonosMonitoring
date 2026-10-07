using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using SonosMonitoring.Api;
using SonosMonitoring.Contracts.Monitoring;
using SonosMonitoring.Integration.Tests.Infrastructure;
using SonosMonitoring.Projections;

namespace SonosMonitoring.E2E.Tests.Monitoring;

/// <summary>Exercises real HTTP, PostgreSQL, the outbox, RabbitMQ and MongoDB as a single backend journey.</summary>
public sealed class MonitoringJourneyTests : IClassFixture<IntegrationInfrastructureFixture>
{
    private readonly IntegrationInfrastructureFixture _infrastructure;

    /// <summary>Receives unique databases and queue names for this fixture.</summary>
    /// <param name="infrastructure">Isolated real dependencies.</param>
    public MonitoringJourneyTests(IntegrationInfrastructureFixture infrastructure) => this._infrastructure = infrastructure;

    /// <summary>Committed work survives delayed projection and API restart; retries do not create duplicates.</summary>
    [Fact]
    public async Task EnableRetryConflictDisableAndRestartReachTheProjectedView()
    {
        var config = this._infrastructure.CreateApiConfigurationOverrides();
        await using var api = ApiHost.Build([], config);
        await StartOnEphemeralPortAsync(api);
        using var client = new HttpClient { BaseAddress = new Uri(api.Urls.Single()) };
        var device = "RINCON_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var requestId = Guid.NewGuid().ToString();
        var accepted = await EnableAsync(client, device, requestId);
        Assert.Equal(1, accepted.EventSequence);
        var pending = await client.GetAsync(accepted.QueryUrl);
        Assert.Equal(HttpStatusCode.Accepted, pending.StatusCode);
        await using var worker = ProjectionHost.Build([], this._infrastructure.ProjectionConfigurationOverrides);
        await StartOnEphemeralPortAsync(worker);
        var view = await AwaitViewAsync(client, accepted.QueryUrl);
        Assert.True(view.Enabled);
        Assert.Equal(15, view.IntervalSeconds);
        Assert.Equal(accepted.ConcurrencyToken, view.ConcurrencyToken);
        var retry = await EnableAsync(client, device, requestId);
        Assert.True(retry.AlreadyCommitted);
        Assert.Equal(accepted.ConcurrencyToken, retry.ConcurrencyToken);
        using var conflictRequest = Request(HttpMethod.Post, $"/api/monitoring/{device}/enable", Guid.NewGuid().ToString(), new EnableMonitoringRequest(15));
        using var conflict = await client.SendAsync(conflictRequest);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await api.StopAsync();

        await using var restarted = ApiHost.Build([], config);
        await StartOnEphemeralPortAsync(restarted);
        using var restartedClient = new HttpClient { BaseAddress = new Uri(restarted.Urls.Single()) };
        var reloaded = await AwaitViewAsync(restartedClient, accepted.QueryUrl);
        Assert.Equal(accepted.ConcurrencyToken, reloaded.ConcurrencyToken);
        using var disableRequest = Request(HttpMethod.Post, $"/api/monitoring/{device}/disable", Guid.NewGuid().ToString(), new DisableMonitoringRequest(reloaded.ConcurrencyToken!));
        using var disabledResponse = await restartedClient.SendAsync(disableRequest);
        Assert.Equal(HttpStatusCode.Accepted, disabledResponse.StatusCode);
        var disabled = (await disabledResponse.Content.ReadFromJsonAsync<MonitoringAccepted>())!;
        var final = await AwaitViewAsync(restartedClient, disabled.QueryUrl);
        Assert.False(final.Enabled);
        Assert.Equal(2, final.EventSequence);
        Assert.Equal(disabled.ConcurrencyToken, final.ConcurrencyToken);
        await restarted.StopAsync();
        await worker.StopAsync();
    }

    /// <summary>Starts actual Kestrel listeners on OS-selected loopback ports.</summary>
    /// <param name="app">Application built through production composition.</param>
    private static async Task StartOnEphemeralPortAsync(WebApplication app)
    {
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
    }

    /// <summary>Creates a new HTTP message without retaining headers across independent actions.</summary>
    /// <param name="method">HTTP method.</param>
    /// <param name="url">Application-relative endpoint.</param>
    /// <param name="key">Stable retry key.</param>
    /// <param name="body">HTTP request DTO.</param>
    private static HttpRequestMessage Request(HttpMethod method, string url, string key, object body)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    /// <summary>Submits a real activation and validates its durable receipt.</summary>
    /// <param name="client">Live API client.</param>
    /// <param name="device">Stable device key.</param>
    /// <param name="requestId">Identity reused only for retries.</param>
    private static async Task<MonitoringAccepted> EnableAsync(HttpClient client, string device, string requestId)
    {
        using var request = Request(HttpMethod.Post, $"/api/monitoring/{device}/enable", requestId, new EnableMonitoringRequest(15));
        using var response = await client.SendAsync(request);
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<MonitoringAccepted>())!;
    }

    /// <summary>Waits outside the aggregate lock for a bounded read-model position.</summary>
    /// <param name="client">Live API client.</param>
    /// <param name="url">Receipt's watermark-qualified query.</param>
    private static async Task<MonitoringResponse> AwaitViewAsync(HttpClient client, string url)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (true)
        {
            using var response = await client.GetAsync(url, deadline.Token);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return (await response.Content.ReadFromJsonAsync<MonitoringResponse>(deadline.Token))!;
            }
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            await Task.Delay(200, deadline.Token);
        }
    }
}
