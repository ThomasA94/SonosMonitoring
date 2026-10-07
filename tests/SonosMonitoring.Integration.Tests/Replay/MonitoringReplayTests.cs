using Devbone.Frameworks.Ddd.Core;
using Devbone.Frameworks.EventBus.Bus;
using Devbone.Frameworks.EventBus.Contracts.Serialization;
using Devbone.Frameworks.EventBus.Outbox.Resend;
using Devbone.Frameworks.EventSourcing.Repositories.Settings;
using Devbone.Frameworks.EventSourcing.Resend;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using SonosMonitoring.Api;
using SonosMonitoring.Application.Monitoring;
using SonosMonitoring.Domain.Monitoring.Disable;
using SonosMonitoring.Domain.Monitoring.Enable;
using SonosMonitoring.Infrastructure.Monitoring;
using SonosMonitoring.Integration.Tests.Infrastructure;
using SonosMonitoring.Platform;
using SonosMonitoring.Projections;

namespace SonosMonitoring.Integration.Tests.Replay;

/// <summary>Checks byte-preserving resend and reconstruction through the real broker and projection pipeline.</summary>
public sealed class MonitoringReplayTests : IClassFixture<IntegrationInfrastructureFixture>
{
    private readonly IntegrationInfrastructureFixture _infrastructure;

    /// <summary>Receives isolated event, broker and projection resources.</summary>
    /// <param name="infrastructure">Fixture that owns all databases and queue names.</param>
    public MonitoringReplayTests(IntegrationInfrastructureFixture infrastructure)
    {
        this._infrastructure = infrastructure;
    }

    /// <summary>Resends identical published envelopes without changing the existing view, then rebuilds in a new database.</summary>
    [Fact]
    public async Task PublishedReplayIsIdempotentAndRebuildsIndependentProjection()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(60));
        CancellationToken token = deadline.Token;
        await using WebApplication sourceWorker = ProjectionHost.Build([], this._infrastructure.ProjectionConfigurationOverrides);
        await using WebApplication api = ApiHost.Build([], this._infrastructure.CreateApiConfigurationOverrides());
        await StartAsync(sourceWorker, token);
        await StartAsync(api, token);
        try
        {
            string deviceKey = "RINCON_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
            string aggregateId = MonitoringIdentity.ForDevice(deviceKey).ToString();
            using IServiceScope scope = api.Services.CreateScope();
            MonitoringExecutor executor = scope.ServiceProvider.GetRequiredService<MonitoringExecutor>();
            var enabled = await executor.Handle(
                [new EnableMonitoring(MonitoringIdentity.ForDevice(deviceKey), RequestId.New, deviceKey, 15)], token);
            Assert.True(enabled.Success, string.Join(", ", enabled.Errors.Select(error => error.Code)));
            var disabled = await executor.Handle(
                [new DisableMonitoring(MonitoringIdentity.ForDevice(deviceKey), RequestId.New,
                    enabled.CommitReceipt!.AggregateConcurrencyToken)], token);
            Assert.True(disabled.Success, string.Join(", ", disabled.Errors.Select(error => error.Code)));

            IMonitoringQuery sourceQuery = api.Services.GetRequiredService<IMonitoringQuery>();
            MonitoringView original = await AwaitViewAsync(sourceQuery, deviceKey, 2, token);
            Assert.False(original.Enabled);
            Assert.Equal(15, original.IntervalSeconds);

            EventStoreSettings settings = api.Configuration.GetSection("EventStore").Get<EventStoreSettings>()
                ?? throw new InvalidOperationException("Missing event-store settings in the test host.");
            PostgresResendRequestStore store = new(settings);
            ResendOptions options = new()
            {
                IsEnabled = true,
                WorkerId = "sonos-replay-it-" + Guid.NewGuid().ToString("N"),
                BatchSize = 10,
                LeaseDuration = TimeSpan.FromSeconds(30),
                PollInterval = TimeSpan.FromSeconds(1),
            };
            ResendProcessor processor = new(store, store,
                api.Services.GetRequiredService<IEventBusResolver>(),
                api.Services.GetRequiredService<ISerializedEventSerializer>(),
                options, NullLogger<ResendProcessor>.Instance);
            ResendRequest duplicateRequest = CreateRequest(aggregateId);
            IReadOnlyList<Devbone.Frameworks.EventSourcing.Outbox.OutboxMessage> originalEnvelopes =
                await AwaitPublishedAsync(store, duplicateRequest, 2, token);
            byte[][] originalBytes = originalEnvelopes.Select(message => message.Envelope.ToArray()).ToArray();
            Assert.Equal(new long[] { 1, 2 }, originalEnvelopes.Select(message => message.EventSequence));

            await store.CreateAsync(duplicateRequest, token);
            Assert.Equal(2, await processor.RunOnceAsync(token));
            ResendRequest duplicateResult = await store.GetAsync(duplicateRequest.ResendRequestId, token)
                ?? throw new InvalidOperationException("Completed duplicate resend request was not found.");
            Assert.Equal(ResendRequestStatus.Completed, duplicateResult.Status);
            Assert.Equal(2, duplicateResult.PublishedCount);
            await Task.Delay(TimeSpan.FromSeconds(1), token);
            MonitoringView afterDuplicate = await AwaitViewAsync(sourceQuery, deviceKey, 2, token);
            Assert.Equal(original, afterDuplicate);
            IReadOnlyList<Devbone.Frameworks.EventSourcing.Outbox.OutboxMessage> retained =
                await store.ReadPublishedAsync(CreateRequest(aggregateId), 10, token);
            Assert.Equal(originalBytes.Length, retained.Count);
            for (int index = 0; index < retained.Count; index++)
            {
                Assert.True(originalBytes[index].SequenceEqual(retained[index].Envelope));
            }

            await using WebApplication rebuildWorker = ProjectionHost.Build([], this._infrastructure.CreateRebuildProjectionConfigurationOverrides());
            await StartAsync(rebuildWorker, token);
            try
            {
                MongoClient mongo = new(this._infrastructure.MongoConnectionString);
                IMonitoringQuery rebuildQuery = new MongoMonitoringQuery(mongo.GetDatabase(this._infrastructure.RebuildMongoDatabaseName));
                string readinessKey = "RINCON_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
                var readiness = await executor.Handle(
                    [new EnableMonitoring(MonitoringIdentity.ForDevice(readinessKey), RequestId.New, readinessKey, 15)], token);
                Assert.True(readiness.Success, string.Join(", ", readiness.Errors.Select(error => error.Code)));
                await AwaitViewAsync(rebuildQuery, readinessKey, 1, token);
                Assert.Null(await rebuildQuery.FindAsync(deviceKey, token));

                ResendRequest rebuildRequest = CreateRequest(aggregateId);
                await store.CreateAsync(rebuildRequest, token);
                Assert.Equal(2, await processor.RunOnceAsync(token));
                ResendRequest rebuildResult = await store.GetAsync(rebuildRequest.ResendRequestId, token)
                    ?? throw new InvalidOperationException("Completed rebuild resend request was not found.");
                Assert.Equal(ResendRequestStatus.Completed, rebuildResult.Status);
                MonitoringView rebuilt = await AwaitViewAsync(rebuildQuery, deviceKey, 2, token);
                Assert.Equal(original.AggregateId, rebuilt.AggregateId);
                Assert.Equal(original.DeviceKey, rebuilt.DeviceKey);
                Assert.Equal(original.Enabled, rebuilt.Enabled);
                Assert.Equal(original.IntervalSeconds, rebuilt.IntervalSeconds);
                Assert.Equal(original.EventSequence, rebuilt.EventSequence);
            }
            finally
            {
                await rebuildWorker.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            await api.StopAsync(CancellationToken.None);
            await sourceWorker.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Creates a valid, narrowly scoped operator request for one aggregate stream.</summary>
    /// <param name="aggregateId">Target stream identity.</param>
    /// <returns>A pending request with a bounded rate and an audit reason.</returns>
    private static ResendRequest CreateRequest(string aggregateId)
    {
        ResendRequest request = new()
        {
            ResendRequestId = Guid.NewGuid().ToString("N"),
            AggregateName = MonitoringNames.AGGREGATE,
            AggregateId = aggregateId,
            RatePerSecond = 1000,
            Reason = "Verify isolated integration replay and projection recovery.",
            RequestedBy = "integration-test",
            RequestedAtUtc = DateTimeOffset.UtcNow,
        };
        request.Validate();
        return request;
    }

    /// <summary>Starts one production host on an OS-selected local port.</summary>
    /// <param name="app">API or projection host.</param>
    /// <param name="token">Overall test deadline.</param>
    /// <returns>A task completed after hosted services start.</returns>
    private static async Task StartAsync(WebApplication app, CancellationToken token)
    {
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(token);
    }

    /// <summary>Waits for a query-visible stream position without relying on a fixed projection delay.</summary>
    /// <param name="query">Real Mongo-backed query port.</param>
    /// <param name="deviceKey">Stable device key.</param>
    /// <param name="sequence">Expected event sequence.</param>
    /// <param name="token">Overall test deadline.</param>
    /// <returns>The projected view at the requested position.</returns>
    private static async Task<MonitoringView> AwaitViewAsync(IMonitoringQuery query, string deviceKey, long sequence,
        CancellationToken token)
    {
        while (true)
        {
            MonitoringView? view = await query.FindAsync(deviceKey, token);
            if (view is not null && view.EventSequence >= sequence)
            {
                return view;
            }

            await Task.Delay(100, token);
        }
    }

    /// <summary>Waits for the durable dispatcher to mark all stream envelopes as published.</summary>
    /// <param name="store">Real PostgreSQL outbox reader.</param>
    /// <param name="request">Single-stream replay scope.</param>
    /// <param name="count">Expected retained envelope count.</param>
    /// <param name="token">Overall test deadline.</param>
    /// <returns>Published envelopes in stream order.</returns>
    private static async Task<IReadOnlyList<Devbone.Frameworks.EventSourcing.Outbox.OutboxMessage>> AwaitPublishedAsync(
        PostgresResendRequestStore store, ResendRequest request, int count, CancellationToken token)
    {
        while (true)
        {
            IReadOnlyList<Devbone.Frameworks.EventSourcing.Outbox.OutboxMessage> rows =
                await store.ReadPublishedAsync(request, count, token);
            if (rows.Count == count)
            {
                return rows;
            }

            await Task.Delay(100, token);
        }
    }

}
