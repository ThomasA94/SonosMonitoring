using Devbone.Frameworks.Ddd.Core;
using Devbone.Frameworks.Ddd.Testing;
using SonosMonitoring.Domain.Monitoring;
using SonosMonitoring.Domain.Monitoring.Disable;
using SonosMonitoring.Domain.Monitoring.Enable;

namespace SonosMonitoring.Unit.Tests.Monitoring;

/// <summary>Verifies monitoring input validation and state-dependent conflicts.</summary>
public sealed class DeviceMonitoringValidationTests
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

    /// <summary>Rejects device keys that are empty, malformed, lower-case, or too long.</summary>
    /// <param name="deviceKey">The malformed device key.</param>
    [Theory]
    [InlineData("")]
    [InlineData("RINCON-")]
    [InlineData("rincon_1234")]
    [InlineData("RINCON_1234-5678")]
    [InlineData("RINCON_1234\n")]
    [InlineData("RINCON_1234\r\n")]
    [InlineData("RINCON_1234\0")]
    [InlineData("RINCON_12345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890")]
    public async Task Enable_rejects_invalid_device_keys(string deviceKey)
    {
        AggregateScenario<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId> scenario = CreateScenario();

        await scenario.WhenAsync(aggregate => aggregate.HandleAsync(
            new EnableMonitoring(aggregate.Id, RequestId.New, deviceKey, 30),
            new EnableMonitoringHandler()));

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldFailWith("monitoring.device.invalid");
        scenario.NewEvents().ShouldBeEmpty();
    }

    /// <summary>Rejects polling intervals outside the inclusive supported range.</summary>
    /// <param name="intervalSeconds">The unsupported polling interval.</param>
    [Theory]
    [InlineData(4)]
    [InlineData(301)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Enable_rejects_intervals_outside_the_supported_range(int intervalSeconds)
    {
        AggregateScenario<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId> scenario = CreateScenario();

        await scenario.WhenAsync(aggregate => aggregate.HandleAsync(
            new EnableMonitoring(aggregate.Id, RequestId.New, DEVICE_KEY, intervalSeconds),
            new EnableMonitoringHandler()));

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldFailWith("monitoring.interval.invalid");
        scenario.NewEvents().ShouldBeEmpty();
    }

    /// <summary>Accepts the two inclusive interval boundaries.</summary>
    /// <param name="intervalSeconds">The supported boundary interval.</param>
    [Theory]
    [InlineData(5)]
    [InlineData(300)]
    public async Task Enable_accepts_interval_boundaries(int intervalSeconds)
    {
        AggregateScenario<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId> scenario = CreateScenario();

        await scenario.WhenCommittedAsync(aggregate => aggregate.HandleAsync(
            new EnableMonitoring(aggregate.Id, RequestId.New, DEVICE_KEY, intervalSeconds),
            new EnableMonitoringHandler()));

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldSucceed();
        Assert.Equal(intervalSeconds, scenario.Aggregate.IntervalSeconds);
    }

    /// <summary>Rejects a second activation while the policy is already enabled.</summary>
    [Fact]
    public async Task Enable_rejects_an_already_enabled_policy()
    {
        AggregateScenario<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId> scenario = CreateScenario();
        await scenario.GivenAsync(aggregate => aggregate.HandleAsync(
            new EnableMonitoring(aggregate.Id, RequestId.New, DEVICE_KEY, 30),
            new EnableMonitoringHandler()));

        await scenario.WhenAsync(aggregate => aggregate.HandleAsync(
            new EnableMonitoring(aggregate.Id, RequestId.New, DEVICE_KEY, 45, aggregate.ConcurrencyToken),
            new EnableMonitoringHandler()));

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldFailWith("monitoring.already_enabled");
        scenario.NewEvents().ShouldBeEmpty();
    }

    /// <summary>Rejects rebinding a policy to a different physical device.</summary>
    [Fact]
    public async Task Enable_rejects_a_different_device_key_after_creation()
    {
        AggregateScenario<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId> scenario = CreateScenario();
        await scenario.GivenAsync(aggregate => aggregate.HandleAsync(
            new EnableMonitoring(aggregate.Id, RequestId.New, DEVICE_KEY, 30),
            new EnableMonitoringHandler()));

        await scenario.WhenAsync(aggregate => aggregate.HandleAsync(
            new EnableMonitoring(aggregate.Id, RequestId.New, "RINCON_DIFFERENT", 45, aggregate.ConcurrencyToken),
            new EnableMonitoringHandler()));

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldFailWith("monitoring.device.mismatch");
        scenario.NewEvents().ShouldBeEmpty();
    }

    /// <summary>Rejects disabling a stream that has not been created.</summary>
    [Fact]
    public async Task Disable_rejects_an_uncreated_policy()
    {
        AggregateScenario<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId> scenario = CreateScenario();

        await scenario.WhenAsync(aggregate => aggregate.HandleAsync(
            new DisableMonitoring(aggregate.Id, RequestId.New),
            new DisableMonitoringHandler()));

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldFailWith("monitoring.not_enabled");
        scenario.NewEvents().ShouldBeEmpty();
    }

    /// <summary>Rejects disabling a policy that is already disabled.</summary>
    [Fact]
    public async Task Disable_rejects_an_already_disabled_policy()
    {
        AggregateScenario<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId> scenario = CreateScenario();
        await scenario.GivenAsync(async aggregate =>
        {
            await aggregate.HandleAsync(
                new EnableMonitoring(aggregate.Id, RequestId.New, DEVICE_KEY, 30),
                new EnableMonitoringHandler());
            return await aggregate.HandleAsync(
                new DisableMonitoring(aggregate.Id, RequestId.New, aggregate.ConcurrencyToken),
                new DisableMonitoringHandler());
        });

        await scenario.WhenAsync(aggregate => aggregate.HandleAsync(
            new DisableMonitoring(aggregate.Id, RequestId.New, aggregate.ConcurrencyToken),
            new DisableMonitoringHandler()));

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldFailWith("monitoring.not_enabled");
        scenario.NewEvents().ShouldBeEmpty();
    }

}
