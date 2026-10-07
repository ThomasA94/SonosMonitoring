using Devbone.Frameworks.Ddd.Core;
using Devbone.Frameworks.Ddd.Results;
using Devbone.Frameworks.Ddd.Testing;
using SonosMonitoring.Domain.Monitoring;
using SonosMonitoring.Domain.Monitoring.Disable;
using SonosMonitoring.Domain.Monitoring.Enable;

namespace SonosMonitoring.Unit.Tests.Monitoring;

/// <summary>Verifies the creation and lifecycle rules for device monitoring policies.</summary>
public sealed class DeviceMonitoringLifecycleTests
{
    private const string DEVICE_KEY = "RINCON_123456789ABC";

    /// <summary>Creates a unique in-memory aggregate scenario for one test.</summary>
    /// <returns>A scenario with no prior event history.</returns>
    private static AggregateScenario<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId> CreateScenario()
    {
        DeviceMonitoringId id = DeviceMonitoringId.New;
        return AggregateScenario.For<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId>(
            id,
            (aggregateId, tracker) => new DeviceMonitoring(aggregateId, tracker));
    }

    /// <summary>Creates a policy with a chosen stable request identity.</summary>
    /// <param name="aggregate">The aggregate receiving the command.</param>
    /// <param name="requestId">The request identity to persist with the event.</param>
    /// <param name="intervalSeconds">The requested polling interval.</param>
    /// <returns>The successful aggregate modification result.</returns>
    private static ValueTask<IAggregateModificationResult> EnableAsync(
        DeviceMonitoring aggregate,
        IRequestId requestId,
        int intervalSeconds = 30) => aggregate.HandleAsync(
            new EnableMonitoring(aggregate.Id, requestId, DEVICE_KEY, intervalSeconds),
            new EnableMonitoringHandler());

    /// <summary>Creates a policy and records its first activation event.</summary>
    [Fact]
    public async Task Enable_on_an_empty_stream_records_creation_and_request_identity()
    {
        AggregateScenario<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId> scenario = CreateScenario();
        IRequestId requestId = RequestId.New;

        await scenario.WhenCommittedAsync(aggregate => EnableAsync(aggregate, requestId));

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldSucceed();
        scenario.NewEvents().ShouldBe("sonosmonitoring.device_monitoring.created");
        Assert.Equal(requestId.ToString(), scenario.StoredEvents().Single().RequestId);
        Assert.True(scenario.Aggregate.Enabled);
        Assert.Equal(DEVICE_KEY, scenario.Aggregate.DeviceKey);
        Assert.Equal(30, scenario.Aggregate.IntervalSeconds);
    }

    /// <summary>Disables an active policy while preserving its device and interval.</summary>
    [Fact]
    public async Task Disable_preserves_the_policy_values_and_records_deactivation()
    {
        AggregateScenario<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId> scenario = CreateScenario();
        await scenario.GivenAsync(aggregate => EnableAsync(aggregate, RequestId.New, 45));

        await scenario.WhenCommittedAsync(aggregate => aggregate.HandleAsync(
            new DisableMonitoring(aggregate.Id, RequestId.New, aggregate.ConcurrencyToken),
            new DisableMonitoringHandler()));

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldSucceed();
        scenario.NewEvents().ShouldBe("sonosmonitoring.device_monitoring.disabled");
        Assert.False(scenario.Aggregate.Enabled);
        Assert.Equal(DEVICE_KEY, scenario.Aggregate.DeviceKey);
        Assert.Equal(45, scenario.Aggregate.IntervalSeconds);
    }

    /// <summary>Re-enables a disabled policy and accepts a new interval for the same device.</summary>
    [Fact]
    public async Task Reenable_records_activation_with_the_new_interval()
    {
        AggregateScenario<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId> scenario = CreateScenario();
        await scenario.GivenAsync(async aggregate =>
        {
            await EnableAsync(aggregate, RequestId.New, 45);
            return await aggregate.HandleAsync(
                new DisableMonitoring(aggregate.Id, RequestId.New, aggregate.ConcurrencyToken),
                new DisableMonitoringHandler());
        });

        await scenario.WhenCommittedAsync(aggregate => aggregate.HandleAsync(
            new EnableMonitoring(aggregate.Id, RequestId.New, DEVICE_KEY, 60, aggregate.ConcurrencyToken),
            new EnableMonitoringHandler()));

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldSucceed();
        scenario.NewEvents().ShouldBe("sonosmonitoring.device_monitoring.enabled");
        Assert.True(scenario.Aggregate.Enabled);
        Assert.Equal(DEVICE_KEY, scenario.Aggregate.DeviceKey);
        Assert.Equal(60, scenario.Aggregate.IntervalSeconds);
    }

    /// <summary>Replays all lifecycle events and restores the complete latest policy.</summary>
    [Fact]
    public async Task Lifecycle_history_rehydrates_the_latest_policy_state()
    {
        AggregateScenario<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId> scenario = CreateScenario();
        await scenario.GivenAsync(async aggregate =>
        {
            await EnableAsync(aggregate, RequestId.New, 25);
            await aggregate.HandleAsync(
                new DisableMonitoring(aggregate.Id, RequestId.New, aggregate.ConcurrencyToken),
                new DisableMonitoringHandler());
            return await aggregate.HandleAsync(
                new EnableMonitoring(aggregate.Id, RequestId.New, DEVICE_KEY, 75, aggregate.ConcurrencyToken),
                new EnableMonitoringHandler());
        });

        Assert.True(scenario.Aggregate.Enabled);
        Assert.Equal(DEVICE_KEY, scenario.Aggregate.DeviceKey);
        Assert.Equal(75, scenario.Aggregate.IntervalSeconds);
        scenario.StoredEvents().ShouldBe(
            "sonosmonitoring.device_monitoring.created",
            "sonosmonitoring.device_monitoring.disabled",
            "sonosmonitoring.device_monitoring.enabled");
    }
}
