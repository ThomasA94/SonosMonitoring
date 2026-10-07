using System.Net;
using System.Net.Http.Json;
using Devbone.Frameworks.EventBus.Bus;
using Devbone.Frameworks.EventBus.Contracts.Serialization;
using Devbone.Frameworks.EventBus.Outbox.Resend;
using Devbone.Frameworks.EventSourcing.Repositories.Settings;
using Devbone.Frameworks.EventSourcing.Resend;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using SonosMonitoring.Api;
using SonosMonitoring.Application.MediaControl;
using SonosMonitoring.Application.MediaControl.Transport;
using SonosMonitoring.Contracts.MediaControl;
using SonosMonitoring.Infrastructure.MediaControl;
using SonosMonitoring.Platform;
using SonosMonitoring.Projections;

namespace SonosMonitoring.Integration.Tests.MediaControl;

/// <summary>Exercises HTTP, event commits, outbox, broker, projection and fake device effects together.</summary>
public sealed class MediaControlJourneyTests : IClassFixture<MediaCoordinationFixture>
{
    private readonly MediaCoordinationFixture _fixture;

    /// <summary>Receives independently provisioned event, coordination, broker and projection resources.</summary>
    /// <param name="fixture">Fixture-owned infrastructure.</param>
    public MediaControlJourneyTests(MediaCoordinationFixture fixture) => this._fixture = fixture;

    /// <summary>A join preserves the displayed destination through HTTP, event commit and projected query.</summary>
    [Fact]
    public async Task Join_preserves_displayed_destination_through_commit_and_query()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(90));
        FakeGroupDeviceGateway device = new();
        await using WebApplication api = this.BuildApi(device);
        await using WebApplication projection = ProjectionHost.Build([], this._fixture.ProjectionConfigurationOverrides);
        await StartAsync(projection, deadline.Token);
        await StartAsync(api, deadline.Token);
        using HttpClient client = new() { BaseAddress = new Uri(api.Urls.Single()) };
        try
        {
            await EnableAsync(client, deadline.Token);
            var source = new MediaTargetContextDto(device.Source.DeviceKey, "household-test", device.Source.DeviceKey, [device.Source.DeviceKey], "source-b");
            var destination = new MediaTargetContextDto(device.Destination.DeviceKey, "household-test", device.Destination.DeviceKey, [device.Destination.DeviceKey], "source-a");
            var body = new MediaActionRequest(source, Reference: destination.DeviceKey, DestinationContext: destination);
            string route = $"/api/media/players/{source.DeviceKey}/join";
            using var stale = Message(HttpMethod.Post, route, Guid.NewGuid(), body with { DestinationContext = destination with { SourceToken = "stale" } });
            using var refused = await client.SendAsync(stale, deadline.Token);
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal(0, device.SendCount);
            Guid id = Guid.NewGuid();
            using var valid = Message(HttpMethod.Post, route, id, body);
            using var accepted = await client.SendAsync(valid, deadline.Token);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            await AwaitOperationAsync(client, id, "desired_state_observed", deadline.Token);
            Assert.Equal(1, device.SendCount);
            using var retry = Message(HttpMethod.Post, route, id, body);
            using var repeated = await client.SendAsync(retry, deadline.Token);
            Assert.Equal(HttpStatusCode.Accepted, repeated.StatusCode);
            using var changed = Message(HttpMethod.Post, route, id, body with { DestinationContext = destination with { SourceToken = "changed" } });
            using var conflict = await client.SendAsync(changed, deadline.Token);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal(1, device.SendCount);
        }
        finally
        {
            await api.StopAsync(CancellationToken.None);
            await projection.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>A committed volume command sends once, projects, deduplicates, conflicts, and replays without a new send.</summary>
    [Fact]
    public async Task Absolute_volume_retry_and_rebuild_never_repeat_a_device_write()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(90));
        FakeMediaDeviceGateway device = new();
        await using WebApplication api = this.BuildApi(device);
        await using WebApplication sourceProjection = ProjectionHost.Build([], this._fixture.ProjectionConfigurationOverrides);
        await StartAsync(sourceProjection, deadline.Token);
        await StartAsync(api, deadline.Token);
        using HttpClient client = new() { BaseAddress = new Uri(api.Urls.Single()) };
        try
        {
            await EnableAsync(client, deadline.Token);
            Guid id = Guid.NewGuid();
            MediaActionRequest request = Volume(device.DeviceKey, 37);
            MediaOperationDto accepted = await SubmitAsync(client, device.DeviceKey, id, request, deadline.Token);
            Assert.Equal("accepted", accepted.Status);
            MediaOperationDto result = await AwaitOperationAsync(client, id, "desired_state_observed", deadline.Token);
            Assert.Equal(3, result.EventSequence);
            Assert.Equal(1, device.SendCount);

            MediaOperationDto retry = await SubmitAsync(client, device.DeviceKey, id, request, deadline.Token);
            Assert.Equal(id, retry.OperationId);
            Assert.Equal(1, device.SendCount);
            using HttpRequestMessage changed = Message(HttpMethod.Post, $"/api/media/players/{device.DeviceKey}/volume",
                id, Volume(device.DeviceKey, 80));
            using HttpResponseMessage conflict = await client.SendAsync(changed, deadline.Token);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal(1, device.SendCount);

            await using WebApplication rebuild = ProjectionHost.Build([], this._fixture.CreateRebuildProjectionConfigurationOverrides());
            await StartAsync(rebuild, deadline.Token);
            try
            {
                MongoClient mongo = new(this._fixture.MongoConnectionString);
                IMediaOperationProjection rebuilt = new MongoMediaOperationProjection(
                    mongo.GetDatabase(this._fixture.RebuildMongoDatabaseName));
                Assert.Null(await rebuilt.SequenceAsync(id, deadline.Token));
                await ReplayAsync(api, id, 3, deadline.Token);
                await AwaitSequenceAsync(rebuilt, id, 3, deadline.Token);
                Assert.Equal(1, device.SendCount);
            }
            finally
            {
                await rebuild.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            await api.StopAsync(CancellationToken.None);
            await sourceProjection.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Revocation between read preparation and durable release prevents a device send.</summary>
    [Fact]
    public async Task Revocation_before_release_prevents_the_device_write()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(60));
        FakeMediaDeviceGateway device = new() { HoldExecutionPreparation = true };
        await using WebApplication api = this.BuildApi(device);
        await using WebApplication projection = ProjectionHost.Build([], this._fixture.ProjectionConfigurationOverrides);
        await StartAsync(projection, deadline.Token);
        await StartAsync(api, deadline.Token);
        using HttpClient client = new() { BaseAddress = new Uri(api.Urls.Single()) };
        try
        {
            await EnableAsync(client, deadline.Token);
            Guid id = Guid.NewGuid();
            await SubmitAsync(client, device.DeviceKey, id, Volume(device.DeviceKey, 45), deadline.Token);
            await device.WaitForExecutionPreparationAsync(deadline.Token);
            using HttpRequestMessage disable = Message(HttpMethod.Post, "/api/media/control/disable", Guid.NewGuid());
            using HttpResponseMessage response = await client.SendAsync(disable, deadline.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            device.ContinueExecutionPreparation();
            MediaOperationDto result = await AwaitOperationAsync(client, id, "revoked", deadline.Token);
            Assert.Equal("revoked", result.Status);
            Assert.Equal(0, device.SendCount);
        }
        finally
        {
            device.ContinueExecutionPreparation();
            await api.StopAsync(CancellationToken.None);
            await projection.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Response loss after one send becomes unknown and a retry cannot trigger a second write.</summary>
    [Fact]
    public async Task Unknown_outcome_and_retry_keep_a_single_write_attempt()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(60));
        FakeMediaDeviceGateway device = new() { ThrowAfterSend = true };
        await using WebApplication api = this.BuildApi(device);
        await using WebApplication projection = ProjectionHost.Build([], this._fixture.ProjectionConfigurationOverrides);
        await StartAsync(projection, deadline.Token);
        await StartAsync(api, deadline.Token);
        using HttpClient client = new() { BaseAddress = new Uri(api.Urls.Single()) };
        try
        {
            await EnableAsync(client, deadline.Token);
            Guid id = Guid.NewGuid();
            MediaActionRequest request = Volume(device.DeviceKey, 54);
            await SubmitAsync(client, device.DeviceKey, id, request, deadline.Token);
            MediaOperationDto result = await AwaitOperationAsync(client, id, "outcome_unknown", deadline.Token);
            Assert.Equal("outcome_unknown", result.Status);
            Assert.Equal(1, device.SendCount);
            await SubmitAsync(client, device.DeviceKey, id, request, deadline.Token);
            Assert.Equal(1, device.SendCount);
            MediaControlStatusDto status = (await client.GetFromJsonAsync<MediaControlStatusDto>("/api/media/control", deadline.Token))!;
            Assert.Contains(status.Blocks, block => block.OperationId == id);

            await api.StopAsync(CancellationToken.None);
            await using WebApplication restarted = this.BuildApi(device);
            await StartAsync(restarted, deadline.Token);
            try
            {
                using HttpClient retryClient = new() { BaseAddress = new Uri(restarted.Urls.Single()) };
                await SubmitAsync(retryClient, device.DeviceKey, id, request, deadline.Token);
                Assert.Equal(1, device.SendCount);
                MediaControlStatusDto retained = (await retryClient.GetFromJsonAsync<MediaControlStatusDto>(
                    "/api/media/control", deadline.Token))!;
                Assert.Contains(retained.Blocks, block => block.OperationId == id);
            }
            finally
            {
                await restarted.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            await api.StopAsync(CancellationToken.None);
            await projection.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>A delayed old effect remains blocked through reads and grant changes until explicit risk acceptance.</summary>
    [Fact]
    public async Task Delayed_effect_remains_fenced_until_exact_risk_acceptance()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(90));
        FakeMediaDeviceGateway device = new() { DelayEffect = true, ThrowAfterSend = true };
        await using WebApplication api = this.BuildApi(device);
        await using WebApplication projection = ProjectionHost.Build([], this._fixture.ProjectionConfigurationOverrides);
        await StartAsync(projection, deadline.Token);
        await StartAsync(api, deadline.Token);
        using HttpClient client = new() { BaseAddress = new Uri(api.Urls.Single()) };
        try
        {
            await EnableAsync(client, deadline.Token);
            Guid oldId = Guid.NewGuid();
            await SubmitAsync(client, device.DeviceKey, oldId, Volume(device.DeviceKey, 80), deadline.Token);
            await AwaitOperationAsync(client, oldId, "outcome_unknown", deadline.Token);
            Assert.Equal(20, device.CurrentVolume);
            MediaPlaybackDto read = (await client.GetFromJsonAsync<MediaPlaybackDto>(
                $"/api/media/players/{device.DeviceKey}/state", deadline.Token))!;
            Assert.Equal(20, read.Volume);

            using (HttpRequestMessage disable = Message(HttpMethod.Post, "/api/media/control/disable", Guid.NewGuid()))
            using (HttpResponseMessage response = await client.SendAsync(disable, deadline.Token))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            await EnableAsync(client, deadline.Token);
            Guid newId = Guid.NewGuid();
            using (HttpRequestMessage blocked = Message(HttpMethod.Post,
                $"/api/media/players/{device.DeviceKey}/volume", newId, Volume(device.DeviceKey, 10)))
            using (HttpResponseMessage response = await client.SendAsync(blocked, deadline.Token))
            {
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            }
            Assert.Equal(1, device.SendCount);

            using (HttpRequestMessage risk = Message(HttpMethod.Post, "/api/media/control/accept-risk", Guid.NewGuid(),
                new AcceptMediaRiskRequest([oldId], true)))
            using (HttpResponseMessage response = await client.SendAsync(risk, deadline.Token))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            using (HttpRequestMessage staleRetry = Message(HttpMethod.Post,
                $"/api/media/players/{device.DeviceKey}/volume", newId, Volume(device.DeviceKey, 10)))
            using (HttpResponseMessage response = await client.SendAsync(staleRetry, deadline.Token))
            {
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            }
            Assert.Equal(1, device.SendCount);
            device.DelayEffect = false;
            device.ThrowAfterSend = false;
            Guid freshId = Guid.NewGuid();
            await SubmitAsync(client, device.DeviceKey, freshId, Volume(device.DeviceKey, 10), deadline.Token);
            await AwaitOperationAsync(client, freshId, "desired_state_observed", deadline.Token);
            Assert.Equal(10, device.CurrentVolume);
            device.ApplyDelayedEffect();
            Assert.Equal(80, device.CurrentVolume);
        }
        finally
        {
            await api.StopAsync(CancellationToken.None);
            await projection.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>A second-step response loss records uncertainty and never resends either released step.</summary>
    [Fact]
    public async Task Multi_step_second_timeout_keeps_each_write_attempt_once_only()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(90));
        FakeMediaDeviceGateway device = new() { StepsPerOperation = 2, ThrowOnSendAttempt = 2 };
        await using WebApplication api = this.BuildApi(device);
        await using WebApplication projection = ProjectionHost.Build([], this._fixture.ProjectionConfigurationOverrides);
        await StartAsync(projection, deadline.Token);
        await StartAsync(api, deadline.Token);
        using HttpClient client = new() { BaseAddress = new Uri(api.Urls.Single()) };
        try
        {
            await EnableAsync(client, deadline.Token);
            Guid id = Guid.NewGuid();
            MediaActionRequest request = Volume(device.DeviceKey, 62);
            await SubmitAsync(client, device.DeviceKey, id, request, deadline.Token);
            MediaOperationDto result = await AwaitOperationAsync(client, id, "outcome_unknown", deadline.Token);
            Assert.Equal("outcome_unknown", result.Status);
            Assert.Equal(2, device.SendCount);
            await SubmitAsync(client, device.DeviceKey, id, request, deadline.Token);
            Assert.Equal(2, device.SendCount);
            PostgresMediaCoordinationStore store = new(new MediaControlOptions
            {
                Enabled = true,
                ConnectionString = this._fixture.ConnectionString,
            });
            CoordinatedMediaOperation persisted = (await store.FindAsync(id, deadline.Token))!;
            Assert.Equal(1, persisted.Step);
            Assert.Equal("outcome_unknown", persisted.Status);
        }
        finally
        {
            await api.StopAsync(CancellationToken.None);
            await projection.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Revocation after one acknowledged step prevents the next send and retains a matching partial result.</summary>
    [Fact]
    public async Task Multi_step_revocation_before_second_release_sends_only_first_step()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(90));
        FakeMediaDeviceGateway device = new() { StepsPerOperation = 2, HoldFirstSendCompletion = true };
        await using WebApplication api = this.BuildApi(device);
        await using WebApplication projection = ProjectionHost.Build([], this._fixture.ProjectionConfigurationOverrides);
        await StartAsync(projection, deadline.Token);
        await StartAsync(api, deadline.Token);
        using HttpClient client = new() { BaseAddress = new Uri(api.Urls.Single()) };
        try
        {
            await EnableAsync(client, deadline.Token);
            Guid id = Guid.NewGuid();
            await SubmitAsync(client, device.DeviceKey, id, Volume(device.DeviceKey, 63), deadline.Token);
            await device.WaitForFirstSendAsync(deadline.Token);
            using (HttpRequestMessage disable = Message(HttpMethod.Post, "/api/media/control/disable", Guid.NewGuid()))
            using (HttpResponseMessage response = await client.SendAsync(disable, deadline.Token))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            device.ContinueFirstSend();
            MediaOperationDto result = await AwaitOperationAsync(client, id, "revoked", deadline.Token);
            Assert.Equal("revoked", result.Status);
            Assert.Equal("media.partial.revoked", result.Code);
            Assert.Equal(0, result.Step);
            Assert.Equal(1, device.SendCount);
            PostgresMediaCoordinationStore store = new(new MediaControlOptions
            {
                Enabled = true,
                ConnectionString = this._fixture.ConnectionString,
            });
            CoordinatedMediaOperation coordination = (await store.FindAsync(id, deadline.Token))!;
            Assert.Equal("revoked", coordination.Status);
            Assert.Equal(0, coordination.Step);
        }
        finally
        {
            device.ContinueFirstSend();
            await api.StopAsync(CancellationToken.None);
            await projection.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>A lost coordinator result write is repaired from the committed terminal domain fact after restart.</summary>
    [Fact]
    public async Task Terminal_domain_result_reconciles_after_coordinator_result_failure()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(90));
        FakeMediaDeviceGateway device = new() { RejectExecutionPreparation = true };
        FailOnceMediaCoordinationStore fault = new(new PostgresMediaCoordinationStore(new MediaControlOptions
        {
            Enabled = true,
            ConnectionString = this._fixture.ConnectionString,
        }));
        await using WebApplication api = this.BuildApi(device, fault);
        await using WebApplication projection = ProjectionHost.Build([], this._fixture.ProjectionConfigurationOverrides);
        await StartAsync(projection, deadline.Token);
        await StartAsync(api, deadline.Token);
        using HttpClient client = new() { BaseAddress = new Uri(api.Urls.Single()) };
        try
        {
            await EnableAsync(client, deadline.Token);
            Guid id = Guid.NewGuid();
            MediaActionRequest request = Volume(device.DeviceKey, 44);
            await SubmitAsync(client, device.DeviceKey, id, request, deadline.Token);
            await fault.WaitForFailureAsync(deadline.Token);
            using (IServiceScope scope = api.Services.CreateScope())
            {
                var domain = await scope.ServiceProvider.GetRequiredService<MediaOperationReader>().ReadAsync(id, deadline.Token);
                Assert.Equal("rejected", domain!.State.Status);
            }
            Assert.Equal(0, device.SendCount);
            Assert.Equal(2, device.PreparationCount);
            await api.StopAsync(CancellationToken.None);

            await using WebApplication restarted = this.BuildApi(device);
            await StartAsync(restarted, deadline.Token);
            try
            {
                using HttpClient retryClient = new() { BaseAddress = new Uri(restarted.Urls.Single()) };
                MediaOperationDto result = await AwaitOperationAsync(retryClient, id, "rejected", deadline.Token);
                Assert.Equal("rejected", result.Status);
                Assert.Equal(0, device.SendCount);
                Assert.Equal(2, device.PreparationCount);
                await SubmitAsync(retryClient, device.DeviceKey, id, request, deadline.Token);
                Assert.Equal(0, device.SendCount);
            }
            finally
            {
                await restarted.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            await api.StopAsync(CancellationToken.None);
            await projection.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>A failed coordinator write after a released definite refusal conservatively keeps the dispatch fenced.</summary>
    [Fact]
    public async Task Released_rejection_result_failure_recovers_as_unknown_without_resend()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(90));
        FakeMediaDeviceGateway device = new() { RejectOnSendAttempt = 1 };
        FailOnceMediaCoordinationStore fault = new(new PostgresMediaCoordinationStore(new MediaControlOptions
        {
            Enabled = true,
            ConnectionString = this._fixture.ConnectionString,
        }));
        await using WebApplication api = this.BuildApi(device, fault);
        await using WebApplication projection = ProjectionHost.Build([], this._fixture.ProjectionConfigurationOverrides);
        await StartAsync(projection, deadline.Token);
        await StartAsync(api, deadline.Token);
        using HttpClient client = new() { BaseAddress = new Uri(api.Urls.Single()) };
        try
        {
            await EnableAsync(client, deadline.Token);
            Guid id = Guid.NewGuid();
            MediaActionRequest request = Volume(device.DeviceKey, 46);
            await SubmitAsync(client, device.DeviceKey, id, request, deadline.Token);
            await fault.WaitForFailureAsync(deadline.Token);
            MediaOperationDto result = await AwaitOperationAsync(client, id, "outcome_unknown", deadline.Token, minimumSequence: 4);
            Assert.Equal(4, result.EventSequence);
            Assert.Equal(1, device.SendCount);
            Assert.Equal(20, device.CurrentVolume);
            PostgresMediaCoordinationStore store = new(new MediaControlOptions
            {
                Enabled = true,
                ConnectionString = this._fixture.ConnectionString,
            });
            CoordinatedMediaOperation coordination = (await store.FindAsync(id, deadline.Token))!;
            Assert.Equal("outcome_unknown", coordination.Status);

            await api.StopAsync(CancellationToken.None);
            await using WebApplication restarted = this.BuildApi(device);
            await StartAsync(restarted, deadline.Token);
            try
            {
                using HttpClient retryClient = new() { BaseAddress = new Uri(restarted.Urls.Single()) };
                await SubmitAsync(retryClient, device.DeviceKey, id, request, deadline.Token);
                Assert.Equal(1, device.SendCount);
                Assert.Equal(20, device.CurrentVolume);
            }
            finally
            {
                await restarted.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            await api.StopAsync(CancellationToken.None);
            await projection.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Builds production API composition while replacing only physical device I/O.</summary>
    /// <param name="device">Deterministic fake device.</param>
    /// <param name="fault">Optional one-shot coordinator failure injector.</param>
    private WebApplication BuildApi(IMediaDeviceGateway device, FailOnceMediaCoordinationStore? fault = null) =>
        ApiHost.Build([], this._fixture.CreateMediaApiConfigurationOverrides(),
        services =>
        {
            services.RemoveAll<IMediaDeviceGateway>();
            services.AddSingleton<IMediaDeviceGateway>(device);
            if (fault is not null)
            {
                services.RemoveAll<IMediaCoordinationStore>();
                services.AddSingleton<IMediaCoordinationStore>(fault);
            }
        });

    /// <summary>Starts a real listener on an OS-selected loopback port.</summary>
    /// <param name="app">API or projection host.</param>
    /// <param name="ct">Overall test deadline.</param>
    private static async Task StartAsync(WebApplication app, CancellationToken ct)
    {
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);
    }

    /// <summary>Creates a command request with one stable idempotency key.</summary>
    /// <param name="method">HTTP method.</param>
    /// <param name="path">API-relative path.</param>
    /// <param name="id">Stable operation identity.</param>
    /// <param name="body">Optional action contract.</param>
    private static HttpRequestMessage Message(HttpMethod method, string path, Guid id, object? body = null)
    {
        HttpRequestMessage message = new(method, path);
        message.Headers.Add("Idempotency-Key", id.ToString("D"));
        if (body is not null)
        {
            message.Content = JsonContent.Create(body);
        }
        return message;
    }

    /// <summary>Enables control through the same protected HTTP endpoint used by operators.</summary>
    /// <param name="client">Live API client.</param>
    /// <param name="ct">Overall test deadline.</param>
    private static async Task EnableAsync(HttpClient client, CancellationToken ct)
    {
        using HttpRequestMessage request = Message(HttpMethod.Post, "/api/media/control/enable", Guid.NewGuid());
        using HttpResponseMessage response = await client.SendAsync(request, ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>Builds the original displayed context for one absolute player volume action.</summary>
    /// <param name="deviceKey">Selected player.</param>
    /// <param name="value">Absolute player volume.</param>
    private static MediaActionRequest Volume(string deviceKey, int value) => new(
        new MediaTargetContextDto(deviceKey, "household-test", deviceKey, [deviceKey], "source-test", "queue-test"),
        Value: value);

    /// <summary>Submits one media intention and reads its accepted receipt.</summary>
    /// <param name="client">Live API client.</param>
    /// <param name="deviceKey">Selected player.</param>
    /// <param name="id">Stable request identity.</param>
    /// <param name="body">Action contract.</param>
    /// <param name="ct">Overall test deadline.</param>
    private static async Task<MediaOperationDto> SubmitAsync(HttpClient client, string deviceKey, Guid id,
        MediaActionRequest body, CancellationToken ct)
    {
        using HttpRequestMessage request = Message(HttpMethod.Post, $"/api/media/players/{deviceKey}/volume", id, body);
        using HttpResponseMessage response = await client.SendAsync(request, ct);
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<MediaOperationDto>(ct))!;
    }

    /// <summary>Polls the authoritative result until its projection watermark is visible.</summary>
    /// <param name="client">Live API client.</param>
    /// <param name="id">Operation identity.</param>
    /// <param name="status">Expected final status.</param>
    /// <param name="ct">Overall test deadline.</param>
    /// <param name="minimumSequence">Required semantic audit watermark, independently of technical fence visibility.</param>
    private static async Task<MediaOperationDto> AwaitOperationAsync(HttpClient client, Guid id, string status,
        CancellationToken ct, long minimumSequence = 0)
    {
        while (true)
        {
            using HttpResponseMessage response = await client.GetAsync($"/api/media/operations/{id:D}?minSequence={minimumSequence}", ct);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                MediaOperationDto view = (await response.Content.ReadFromJsonAsync<MediaOperationDto>(ct))!;
                if (view.Status == status)
                {
                    return view;
                }
            }
            else
            {
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            }
            await Task.Delay(150, ct);
        }
    }

    /// <summary>Waits for a rebuilt Mongo document to cover all committed events.</summary>
    /// <param name="projection">Independent Mongo query.</param>
    /// <param name="id">Operation identity.</param>
    /// <param name="sequence">Expected last event sequence.</param>
    /// <param name="ct">Overall test deadline.</param>
    private static async Task AwaitSequenceAsync(IMediaOperationProjection projection, Guid id, long sequence,
        CancellationToken ct)
    {
        while ((await projection.SequenceAsync(id, ct) ?? 0) < sequence)
        {
            await Task.Delay(150, ct);
        }
    }

    /// <summary>Resends the original immutable operation envelopes to an independent projection worker.</summary>
    /// <param name="api">Source host with the production outbox publisher.</param>
    /// <param name="id">Original operation.</param>
    /// <param name="count">Expected retained events.</param>
    /// <param name="ct">Overall test deadline.</param>
    private static async Task ReplayAsync(WebApplication api, Guid id, int count, CancellationToken ct)
    {
        EventStoreSettings settings = api.Configuration.GetSection("EventStore").Get<EventStoreSettings>()!;
        PostgresResendRequestStore store = new(settings);
        ResendRequest request = new()
        {
            ResendRequestId = Guid.NewGuid().ToString("N"),
            AggregateName = MediaControlNames.AGGREGATE,
            AggregateId = SonosMonitoring.Domain.MediaControl.MediaOperationId.With(id).ToString(),
            RatePerSecond = 1000,
            Reason = "Verify isolated media projection rebuild without device execution.",
            RequestedBy = "integration-test",
            RequestedAtUtc = DateTimeOffset.UtcNow,
        };
        request.Validate();
        while ((await store.ReadPublishedAsync(request, count, ct)).Count < count)
        {
            await Task.Delay(150, ct);
        }
        ResendOptions options = new()
        {
            IsEnabled = true,
            WorkerId = "media-replay-it-" + Guid.NewGuid().ToString("N"),
            BatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(30),
            PollInterval = TimeSpan.FromSeconds(1),
        };
        ResendProcessor processor = new(store, store, api.Services.GetRequiredService<IEventBusResolver>(),
            api.Services.GetRequiredService<ISerializedEventSerializer>(), options,
            NullLogger<ResendProcessor>.Instance);
        await store.CreateAsync(request, ct);
        Assert.Equal(count, await processor.RunOnceAsync(ct));
    }
}
