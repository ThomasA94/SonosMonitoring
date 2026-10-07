using Devbone.Frameworks.Ddd.Aggregates;
using Devbone.Frameworks.Ddd.Aggregates.Infrastructure;
using Devbone.Frameworks.Ddd.Core;
using Devbone.Frameworks.Ddd.Testing;
using SonosMonitoring.Domain.Monitoring;
using SonosMonitoring.Domain.Monitoring.Enable;
using SonosMonitoring.Domain.Monitoring.Events;
using SonosMonitoring.Platform;

namespace SonosMonitoring.Unit.Tests.Monitoring;

/// <summary>Verifies durable event payload compatibility and request identity preservation.</summary>
public sealed class DeviceMonitoringPersistenceContractTests
{
    private const string HISTORICAL_CREATED_PAYLOAD = "{\"deviceKey\":\"RINCON_123456789ABC\",\"enabled\":true,\"intervalSeconds\":30}";

    /// <summary>Deserializes the original creation payload through the production event serializer.</summary>
    [Fact]
    public void Historical_creation_payload_remains_readable()
    {
        SystemTextJsonAggregateEventSerializer serializer = SystemTextJsonAggregateEventSerializer.Instance;

        DeviceMonitoringRegistered restored = Assert.IsType<DeviceMonitoringRegistered>(serializer.DeserializeEvent(
            HISTORICAL_CREATED_PAYLOAD,
            typeof(DeviceMonitoringRegistered),
            1));

        Assert.Equal("RINCON_123456789ABC", restored.DeviceKey);
        Assert.True(restored.Enabled);
        Assert.Equal(30, restored.IntervalSeconds);
    }

    /// <summary>Round-trips a newly committed creation payload through the production serializer.</summary>
    [Fact]
    public async Task Current_creation_payload_roundtrips_through_the_default_serializer()
    {
        DeviceMonitoringId id = DeviceMonitoringId.New;
        AggregateScenario<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId> scenario =
            AggregateScenario.For<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId>(
                id,
                (aggregateId, tracker) => new DeviceMonitoring(aggregateId, tracker));

        await scenario.WhenCommittedAsync(aggregate => aggregate.HandleAsync(
            new EnableMonitoring(aggregate.Id, RequestId.New, "RINCON_123456789ABC", 30),
            new EnableMonitoringHandler()));

        var storedEvent = Assert.Single(scenario.StoredEvents());
        DeviceMonitoringRegistered restored = Assert.IsType<DeviceMonitoringRegistered>(
            SystemTextJsonAggregateEventSerializer.Instance.DeserializeEvent(
                storedEvent.Value,
                typeof(DeviceMonitoringRegistered),
                storedEvent.EventVersion));

        Assert.Equal("RINCON_123456789ABC", restored.DeviceKey);
        Assert.True(restored.Enabled);
        Assert.Equal(30, restored.IntervalSeconds);
    }

    /// <summary>Commits an enable event and confirms its request key is recorded for deduplication.</summary>
    [Fact]
    public async Task Created_event_retains_the_request_id_used_for_idempotent_retries()
    {
        DeviceMonitoringId id = DeviceMonitoringId.New;
        AggregateScenario<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId> scenario =
            AggregateScenario.For<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId>(
                id,
                (aggregateId, tracker) => new DeviceMonitoring(aggregateId, tracker));
        IRequestId requestId = RequestId.New;

        await scenario.WhenCommittedAsync(aggregate => aggregate.HandleAsync(
            new EnableMonitoring(aggregate.Id, requestId, "RINCON_123456789ABC", 30),
            new EnableMonitoringHandler()));

        Assert.Equal(requestId.ToString(), scenario.StoredEvents().Single().RequestId);
        Assert.True(scenario.Repository.HasProcessedRequest(
            new AggregateName(MonitoringNames.AGGREGATE),
            id,
            requestId));
    }
}
