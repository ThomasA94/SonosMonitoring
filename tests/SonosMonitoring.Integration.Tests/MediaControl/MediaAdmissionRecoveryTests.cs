using System.Net;
using System.Net.Http.Json;
using Devbone.Frameworks.EventSourcing.Repositories.Settings;
using Devbone.Frameworks.EventSourcing.Resend;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SonosMonitoring.Api;
using SonosMonitoring.Application.MediaControl;
using SonosMonitoring.Application.MediaControl.Transport;
using SonosMonitoring.Contracts.MediaControl;
using SonosMonitoring.Domain.MediaControl;
using SonosMonitoring.Infrastructure.MediaControl;
using SonosMonitoring.Platform;
using SonosMonitoring.Projections;

namespace SonosMonitoring.Integration.Tests.MediaControl;

/// <summary>Verifies recovery of event-committed intentions whose queue admission failed.</summary>
public sealed class MediaAdmissionRecoveryTests : IClassFixture<MediaCoordinationFixture>
{
    private readonly MediaCoordinationFixture _fixture;

    /// <summary>Receives disposable real PostgreSQL, RabbitMQ and MongoDB resources.</summary>
    /// <param name="fixture">Isolated infrastructure owner.</param>
    public MediaAdmissionRecoveryTests(MediaCoordinationFixture fixture) => this._fixture = fixture;

    /// <summary>Restart recovery closes the original intent without a polling request or any device send.</summary>
    /// <param name="revoke">Revokes the captured grant before restart instead of waiting for expiry.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_admission_is_closed_after_restart_and_retry_preserves_original_intention(bool revoke)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(90));
        var store = this.Store();
        FakeMediaDeviceGateway device = new();
        FailOnceMediaCoordinationStore fault = new(store, failAdmission: true);
        await using WebApplication projection = ProjectionHost.Build([], this._fixture.ProjectionConfigurationOverrides);
        await using WebApplication api = this.BuildApi(device, fault);
        await StartAsync(projection, deadline.Token);
        await StartAsync(api, deadline.Token);
        using HttpClient client = new() { BaseAddress = new Uri(api.Urls.Single()) };
        Guid id = Guid.NewGuid();
        MediaActionRequest body = new(new MediaTargetContextDto(device.DeviceKey, "household-test", device.DeviceKey,
            [device.DeviceKey], "source-test", "queue-test"), Value: 41);
        try
        {
            using (var enable = Message("/api/media/control/enable", Guid.NewGuid()))
            using (var response = await client.SendAsync(enable, deadline.Token))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            using (var request = Message($"/api/media/players/{device.DeviceKey}/volume", id, body))
            using (var response = await client.SendAsync(request, deadline.Token))
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            }
            MediaOperationView original;
            using (var scope = api.Services.CreateScope())
            {
                original = (await scope.ServiceProvider.GetRequiredService<MediaOperationReader>().ReadAsync(id, deadline.Token))!;
                Assert.Equal("accepted", original.State.Status);
            }
            Assert.Null(await store.FindAsync(id, deadline.Token));
            // Isolate the admission failure from a separate publish-before-settlement crash window.
            // Such an outbox claim may legally remain leased for five minutes after shutdown.
            await AwaitPublishedRequestAsync(api, id, deadline.Token);
            await api.StopAsync(deadline.Token);
            if (revoke)
            {
                await store.SetGrantAsync(false, Guid.NewGuid(), original.State.Generation, deadline.Token);
            }
            else
            {
                TimeSpan remaining = original.State.ExpiresAtUtc - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining.Add(TimeSpan.FromMilliseconds(100)), deadline.Token);
                }
            }

            await using WebApplication restarted = this.BuildApi(device);
            await StartAsync(restarted, deadline.Token);
            try
            {
                string outcome = revoke ? "revoked" : "expired";
                MediaOperationView result;
                // Read the event port directly: this proves the background worker, not GET, performs recovery.
                while (true)
                {
                    using var scope = restarted.Services.CreateScope();
                    result = (await scope.ServiceProvider.GetRequiredService<MediaOperationReader>().ReadAsync(id, deadline.Token))!;
                    if (result.State.Status == outcome)
                    {
                        break;
                    }
                    await Task.Delay(150, deadline.Token);
                }
                Assert.Equal(2, result.EventSequence);
                Assert.Equal(original.State.Generation, result.State.Generation);
                Assert.Equal(original.State.ExpiresAtUtc, result.State.ExpiresAtUtc);
                Assert.Equal(original.State.PayloadJson, result.State.PayloadJson);
                var closed = (await store.FindAsync(id, deadline.Token))!;
                Assert.Equal(outcome, closed.Status);
                Assert.Equal(-1, closed.Step);
                Assert.Equal(0, device.SendCount);
                using HttpClient retryClient = new() { BaseAddress = new Uri(restarted.Urls.Single()) };
                using (var retry = Message($"/api/media/players/{device.DeviceKey}/volume", id, body))
                using (var response = await retryClient.SendAsync(retry, deadline.Token))
                {
                    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
                    Assert.Equal(outcome, (await response.Content.ReadFromJsonAsync<MediaOperationDto>(deadline.Token))!.Status);
                }
                DateTimeOffset visibilityDeadline = DateTimeOffset.UtcNow.AddSeconds(30);
                while (true)
                {
                    using var response = await retryClient.GetAsync($"/api/media/operations/{id:D}", deadline.Token);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        Assert.Equal(outcome, (await response.Content.ReadFromJsonAsync<MediaOperationDto>(deadline.Token))!.Status);
                        break;
                    }
                    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
                    if (DateTimeOffset.UtcNow >= visibilityDeadline)
                    {
                        long? projected = await restarted.Services.GetRequiredService<IMediaOperationProjection>().SequenceAsync(id, deadline.Token);
                        await using NpgsqlConnection diagnosticConnection = new(this._fixture.ConnectionString);
                        await diagnosticConnection.OpenAsync(deadline.Token);
                        await using NpgsqlCommand diagnostic = new("""
                            SELECT string_agg(jsonb_build_object(
                                'sequence', to_jsonb(o)->'EventSequence', 'status', to_jsonb(o)->'Status',
                                'leaseExpiresAtUtc', to_jsonb(o)->'LeaseExpiresAtUtc',
                                'nextAttemptAtUtc', to_jsonb(o)->'NextAttemptAtUtc')::text, E'\n')
                            FROM event_outbox o WHERE "AggregateId"=@id
                            """, diagnosticConnection);
                        diagnostic.Parameters.AddWithValue("id", MediaOperationId.With(id).ToString());
                        string metadata = (string)(await diagnostic.ExecuteScalarAsync(deadline.Token))!;
                        Assert.Fail($"Projection timed out: semantic={result.EventSequence}, merged={projected}; outbox={metadata}");
                    }
                    await Task.Delay(150, deadline.Token);
                }
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

    /// <summary>Expired close and admission race to the same immutable tombstone, never to executable work.</summary>
    [Fact]
    public async Task Closing_expired_unadmitted_intention_is_atomic_with_concurrent_admission()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
        var store = this.Store();
        var grant = await store.StatusAsync(deadline.Token);
        grant = await store.SetGrantAsync(true, Guid.NewGuid(), grant.Generation, deadline.Token);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        CoordinatedMediaOperation original = new(Guid.NewGuid(), 0, new string('a', 64), grant.Generation,
            ["RINCON_RECOVERY_RACE"], "{}", now.AddMinutes(-1), now.AddSeconds(-1));
        var close = store.CloseAdmissionAsync(original, deadline.Token);
        var admission = Record.ExceptionAsync(async () => await this.Store().AdmitAsync(original, deadline.Token));
        Assert.Equal("expired", (await close)!.Status);
        var exception = await admission;
        Assert.True(exception is null or MediaCoordinationException);
        var persisted = (await store.FindAsync(original.OperationId, deadline.Token))!;
        Assert.Equal("expired", persisted.Status);
        Assert.Equal(-1, persisted.Step);
        Assert.Equal(persisted.Position, (await store.CloseAdmissionAsync(original, deadline.Token))!.Position);
        Assert.Equal("expired", (await store.AdmitAsync(original, deadline.Token)).Status);
        // This store-only race intentionally has no event stream; keep it out of later host audit recovery.
        await store.MarkRecordedAsync(original.OperationId, "expired", deadline.Token);
    }

    /// <summary>Creates a new coordinator for the fixture-owned database.</summary>
    private PostgresMediaCoordinationStore Store() => new(new MediaControlOptions
    {
        Enabled = true,
        ConnectionString = this._fixture.ConnectionString,
    });

    /// <summary>Establishes completed outbox settlement before testing an unrelated admission recovery restart.</summary>
    /// <param name="api">Original API host.</param>
    /// <param name="id">Committed operation identity.</param>
    /// <param name="ct">Overall test deadline.</param>
    private static async Task AwaitPublishedRequestAsync(WebApplication api, Guid id, CancellationToken ct)
    {
        EventStoreSettings settings = api.Configuration.GetSection("EventStore").Get<EventStoreSettings>()!;
        PostgresResendRequestStore outbox = new(settings);
        ResendRequest descriptor = new()
        {
            ResendRequestId = Guid.NewGuid().ToString("N"),
            AggregateName = MediaControlNames.AGGREGATE,
            AggregateId = MediaOperationId.With(id).ToString(),
            RatePerSecond = 1000,
            Reason = "Observe settlement before isolated admission-recovery restart.",
            RequestedBy = "integration-test",
            RequestedAtUtc = DateTimeOffset.UtcNow,
        };
        descriptor.Validate();
        while ((await outbox.ReadPublishedAsync(descriptor, 1, ct)).Count < 1)
        {
            await Task.Delay(50, ct);
        }
    }

    /// <summary>Builds the production host with physical I/O replaced and a short test dispatch lifetime.</summary>
    /// <param name="device">Deterministic device transport.</param>
    /// <param name="store">Optional admission fault injector.</param>
    private WebApplication BuildApi(IMediaDeviceGateway device, IMediaCoordinationStore? store = null)
    {
        Dictionary<string, string?> config = new(this._fixture.CreateMediaApiConfigurationOverrides())
        {
            ["MediaControl:LifetimeSeconds"] = "5",
        };
        return ApiHost.Build([], config, services =>
        {
            services.RemoveAll<IMediaDeviceGateway>();
            services.AddSingleton(device);
            if (store is not null)
            {
                services.RemoveAll<IMediaCoordinationStore>();
                services.AddSingleton(store);
            }
        });
    }

    /// <summary>Starts a real loopback listener on an OS-selected port.</summary>
    /// <param name="app">Host under test.</param>
    /// <param name="ct">Overall test deadline.</param>
    private static async Task StartAsync(WebApplication app, CancellationToken ct)
    {
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);
    }

    /// <summary>Builds a request with its immutable operation identity.</summary>
    /// <param name="path">Protected API route.</param>
    /// <param name="id">Stable user action identity.</param>
    /// <param name="body">Optional media action.</param>
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
}
