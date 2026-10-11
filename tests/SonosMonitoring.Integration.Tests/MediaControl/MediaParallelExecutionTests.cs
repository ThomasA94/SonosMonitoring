using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SonosMonitoring.Api;
using SonosMonitoring.Application.MediaControl;
using SonosMonitoring.Application.MediaControl.Transport;
using SonosMonitoring.Contracts.MediaControl;
using SonosMonitoring.Infrastructure.MediaControl;
using SonosMonitoring.Projections;

namespace SonosMonitoring.Integration.Tests.MediaControl;

/// <summary>Verifies the production executor can overlap disjoint targets while fencing same-target work.</summary>
public sealed class MediaParallelExecutionTests : IClassFixture<MediaCoordinationFixture>
{
    private readonly MediaCoordinationFixture _fixture;

    /// <summary>Receives isolated PostgreSQL and MongoDB resources.</summary>
    /// <param name="fixture">Fixture-owned infrastructure.</param>
    public MediaParallelExecutionTests(MediaCoordinationFixture fixture) => this._fixture = fixture;

    /// <summary>Completes disjoint B while A is blocked and holds a second A write until the first A finishes.</summary>
    [Fact]
    public async Task Disjoint_operation_runs_while_same_target_operation_waits()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(90));
        string firstTarget = "RINCON_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        string secondTarget = "RINCON_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        const int FIRST_A_VALUE = 31;
        const int B_VALUE = 44;
        const int SECOND_A_VALUE = 58;
        BlockingParallelMediaDeviceGateway device = new(firstTarget, FIRST_A_VALUE);
        await using WebApplication api = this.BuildApi(device);
        await using WebApplication projection = ProjectionHost.Build([], this._fixture.ProjectionConfigurationOverrides);
        await StartAsync(projection, deadline.Token);
        await StartAsync(api, deadline.Token);
        using HttpClient client = new() { BaseAddress = new Uri(api.Urls.Single()) };

        Guid firstAId = Guid.NewGuid();
        Guid bId = Guid.NewGuid();
        Guid secondAId = Guid.NewGuid();
        try
        {
            await EnableAsync(client, deadline.Token);
            await SubmitAsync(client, firstTarget, firstAId, Volume(firstTarget, FIRST_A_VALUE), deadline.Token);
            await device.WaitForHeldSendAsync(deadline.Token);

            await SubmitAsync(client, secondTarget, bId, Volume(secondTarget, B_VALUE), deadline.Token);
            using CancellationTokenSource bDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            bDeadline.CancelAfter(TimeSpan.FromSeconds(15));
            await AwaitOperationAsync(client, bId, "desired_state_observed", bDeadline.Token);
            Assert.Equal(1, device.SendCountFor(B_VALUE));

            await SubmitAsync(client, firstTarget, secondAId, Volume(firstTarget, SECOND_A_VALUE), deadline.Token);
            IMediaCoordinationStore store = api.Services.GetRequiredService<IMediaCoordinationStore>();
            CoordinatedMediaOperation queuedA = await store.FindAsync(secondAId, deadline.Token)
                ?? throw new InvalidOperationException("The second A operation was not admitted.");
            Assert.Equal("accepted", queuedA.Status);

            await Task.Delay(TimeSpan.FromMilliseconds(900), deadline.Token);
            Assert.Equal(0, device.SendCountFor(SECOND_A_VALUE));

            device.ReleaseHeldSend();
            await AwaitOperationAsync(client, firstAId, "desired_state_observed", deadline.Token);
            await AwaitOperationAsync(client, secondAId, "desired_state_observed", deadline.Token);

            Assert.Equal(1, device.SendCountFor(FIRST_A_VALUE));
            Assert.Equal(1, device.SendCountFor(SECOND_A_VALUE));
            Assert.Equal(1, device.SendCountFor(B_VALUE));
            Assert.Equal(1L, await this.ReadStepCountAsync(firstAId, deadline.Token));
            Assert.Equal(1L, await this.ReadStepCountAsync(secondAId, deadline.Token));
            Assert.Equal(1L, await this.ReadStepCountAsync(bId, deadline.Token));
        }
        finally
        {
            device.ReleaseHeldSend();
            await api.StopAsync(CancellationToken.None);
            await projection.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Builds the production API composition and replaces only speaker I/O.</summary>
    /// <param name="device">Deterministic device simulator.</param>
    /// <returns>A production API host using isolated fixture resources.</returns>
    private WebApplication BuildApi(IMediaDeviceGateway device)
    {
        Dictionary<string, string?> configuration = this._fixture.CreateMediaApiConfigurationOverrides()
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        configuration["MediaControl:MaxConcurrentExecutions"] = "3";
        return ApiHost.Build([], configuration, services =>
        {
            services.RemoveAll<IMediaDeviceGateway>();
            services.AddSingleton<IMediaDeviceGateway>(device);
        });
    }

    /// <summary>Starts a listener on an OS-selected loopback port.</summary>
    /// <param name="app">API or projection host.</param>
    /// <param name="ct">Overall test deadline.</param>
    private static async Task StartAsync(WebApplication app, CancellationToken ct)
    {
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);
    }

    /// <summary>Sends an idempotent HTTP command envelope.</summary>
    /// <param name="path">API path.</param>
    /// <param name="id">Stable request identity.</param>
    /// <param name="body">Optional request payload.</param>
    private static HttpRequestMessage Message(string path, Guid id, object? body = null)
    {
        HttpRequestMessage request = new(HttpMethod.Post, path);
        request.Headers.Add("Idempotency-Key", id.ToString("D"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    /// <summary>Enables the media executor through its operator endpoint.</summary>
    /// <param name="client">Live API client.</param>
    /// <param name="ct">Overall test deadline.</param>
    private static async Task EnableAsync(HttpClient client, CancellationToken ct)
    {
        using HttpRequestMessage request = Message("/api/media/control/enable", Guid.NewGuid());
        using HttpResponseMessage response = await client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Builds an absolute player-volume command with the simulator's stable context.</summary>
    /// <param name="deviceKey">Selected player identity.</param>
    /// <param name="value">Desired volume.</param>
    /// <returns>Volume request contract.</returns>
    private static MediaActionRequest Volume(string deviceKey, int value) => new(
        new MediaTargetContextDto(deviceKey, "household-test", deviceKey, [deviceKey], "source-test", "queue-test"),
        Value: value);

    /// <summary>Submits an operation and verifies that the API accepted its immutable intent.</summary>
    /// <param name="client">Live API client.</param>
    /// <param name="deviceKey">Selected player identity.</param>
    /// <param name="id">Stable operation identity.</param>
    /// <param name="body">Volume command.</param>
    /// <param name="ct">Overall test deadline.</param>
    private static async Task SubmitAsync(HttpClient client, string deviceKey, Guid id, MediaActionRequest body, CancellationToken ct)
    {
        using HttpRequestMessage request = Message($"/api/media/players/{deviceKey}/volume", id, body);
        using HttpResponseMessage response = await client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    /// <summary>Waits until an operation's projected result reaches the expected terminal state.</summary>
    /// <param name="client">Live API client.</param>
    /// <param name="id">Operation identity.</param>
    /// <param name="status">Expected final state.</param>
    /// <param name="ct">Bounded wait.</param>
    private static async Task AwaitOperationAsync(HttpClient client, Guid id, string status, CancellationToken ct)
    {
        while (true)
        {
            using HttpResponseMessage response = await client.GetAsync($"/api/media/operations/{id:D}", ct);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                MediaOperationDto operation = (await response.Content.ReadFromJsonAsync<MediaOperationDto>(ct))!;
                if (operation.Status == status)
                {
                    return;
                }
            }
            else
            {
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150), ct);
        }
    }

    /// <summary>Counts durable step claims to ensure each operation released exactly one write.</summary>
    /// <param name="id">Operation identity.</param>
    /// <param name="ct">Overall test deadline.</param>
    /// <returns>Number of persisted write claims.</returns>
    private async Task<long> ReadStepCountAsync(Guid id, CancellationToken ct)
    {
        await using NpgsqlConnection connection = new(this._fixture.ConnectionString);
        await connection.OpenAsync(ct);
        await using NpgsqlCommand command = new("SELECT count(*) FROM sonos_media.steps WHERE operation=@id", connection);
        command.Parameters.AddWithValue("id", id);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }
}
