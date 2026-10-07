using Devbone.Frameworks.Ddd.Core;
using Devbone.Frameworks.Ddd.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SonosMonitoring.Application.MediaControl;
using SonosMonitoring.Domain.MediaControl;
using SonosMonitoring.Domain.MediaControl.Requests;

namespace SonosMonitoring.Unit.Tests.MediaControl;

/// <summary>Checks durable media intention and irreversible dispatch rules without device infrastructure.</summary>
public sealed class MediaOperationTests
{
    private const string DEVICE_KEY = "RINCON_123456789ABC";

    /// <summary>Creates an uncommitted operation scenario.</summary>
    private static AggregateScenario<MediaControlOperation, MediaOperationState, MediaOperationId> Scenario()
    {
        MediaOperationId id = MediaOperationId.New;
        return AggregateScenario.For<MediaControlOperation, MediaOperationState, MediaOperationId>(id,
            (operationId, tracker) => new MediaControlOperation(operationId, tracker));
    }

    /// <summary>Creates a valid absolute volume intention.</summary>
    /// <param name="aggregate">Target operation aggregate.</param>
    /// <param name="volume">Requested absolute volume.</param>
    private static ChoosePlayerVolume Volume(MediaControlOperation aggregate, int volume) => new(
        aggregate.Id, RequestId.New, DEVICE_KEY, "{}", "fingerprint", "grant-1",
        new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 30, 12, 1, 0, TimeSpan.Zero),
        new MediaIntentInputs(Value: volume));

    /// <summary>Persists the full request, dispatch, and separately recorded result.</summary>
    [Fact]
    public async Task Lifecycle_records_accepted_dispatch_started_and_device_acknowledged_facts()
    {
        var scenario = Scenario();
        await scenario.GivenAsync(aggregate => aggregate.HandleAsync(Volume(aggregate, 37), new ChoosePlayerVolumeHandler()));
        await scenario.WhenCommittedAsync(async aggregate =>
        {
            await aggregate.HandleAsync(new StartMediaDispatch(aggregate.Id, RequestId.New,
                new DateTimeOffset(2026, 9, 30, 12, 0, 15, TimeSpan.Zero), 0, aggregate.ConcurrencyToken!),
                new StartMediaDispatchHandler());
            return await aggregate.HandleAsync(new CompleteMediaResult(aggregate.Id, RequestId.New,
                "device_acknowledged", "", aggregate.ConcurrencyToken!), new CompleteMediaResultHandler());
        });

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldSucceed();
        scenario.StoredEvents().ShouldBe("sonosmonitoring.media_control.operation.created",
            "sonosmonitoring.media_control.dispatch_started", "sonosmonitoring.media_control.result_recorded");
        Assert.Equal("device_acknowledged", scenario.Aggregate.State.Status);
        Assert.Equal(0, scenario.Aggregate.State.Step);
        Assert.Equal("volume", scenario.Aggregate.State.Action);
        Assert.Equal("grant-1", scenario.Aggregate.State.Generation);
    }

    /// <summary>Rejects a second dispatch attempt for the same writing step.</summary>
    [Fact]
    public async Task Same_step_cannot_begin_twice_after_dispatch_commit()
    {
        var scenario = Scenario();
        await scenario.GivenAsync(async aggregate =>
        {
            await aggregate.HandleAsync(Volume(aggregate, 37), new ChoosePlayerVolumeHandler());
            return await aggregate.HandleAsync(new StartMediaDispatch(aggregate.Id, RequestId.New,
                new DateTimeOffset(2026, 9, 30, 12, 0, 15, TimeSpan.Zero), 0, aggregate.ConcurrencyToken!),
                new StartMediaDispatchHandler());
        });
        await scenario.WhenAsync(aggregate => aggregate.HandleAsync(new StartMediaDispatch(aggregate.Id,
            RequestId.New, new DateTimeOffset(2026, 9, 30, 12, 0, 20, TimeSpan.Zero), 0,
            aggregate.ConcurrencyToken!), new StartMediaDispatchHandler()));

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldFailWith("media.dispatch.invalid");
        scenario.NewEvents().ShouldBeEmpty();
    }

    /// <summary>Accepts only the immediate next step after the previous step was acknowledged.</summary>
    [Fact]
    public async Task Acknowledged_step_allows_only_its_successor()
    {
        var scenario = Scenario();
        await scenario.GivenAsync(async aggregate =>
        {
            await aggregate.HandleAsync(Volume(aggregate, 37), new ChoosePlayerVolumeHandler());
            await aggregate.HandleAsync(new StartMediaDispatch(aggregate.Id, RequestId.New,
                new DateTimeOffset(2026, 9, 30, 12, 0, 15, TimeSpan.Zero), 0, aggregate.ConcurrencyToken!),
                new StartMediaDispatchHandler());
            return await aggregate.HandleAsync(new CompleteMediaResult(aggregate.Id, RequestId.New,
                "device_acknowledged", "", aggregate.ConcurrencyToken!), new CompleteMediaResultHandler());
        });
        await scenario.WhenCommittedAsync(aggregate => aggregate.HandleAsync(new StartMediaDispatch(aggregate.Id,
            RequestId.New, new DateTimeOffset(2026, 9, 30, 12, 0, 20, TimeSpan.Zero), 1,
            aggregate.ConcurrencyToken!), new StartMediaDispatchHandler()));

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldSucceed();
        Assert.Equal(1, scenario.Aggregate.State.Step);
        Assert.Equal("dispatch_started", scenario.Aggregate.State.Status);
    }

    /// <summary>Rejects invalid volume before an event is emitted.</summary>
    [Fact]
    public async Task Volume_outside_the_supported_range_is_rejected()
    {
        var scenario = Scenario();
        await scenario.WhenAsync(aggregate => aggregate.HandleAsync(Volume(aggregate, 101),
            new ChoosePlayerVolumeHandler()));

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldFailWith("media.input.invalid");
        scenario.NewEvents().ShouldBeEmpty();
    }

    /// <summary>Records a control decision without a device dispatch marker.</summary>
    [Fact]
    public async Task Disable_control_decision_completes_directly_from_requested()
    {
        var scenario = Scenario();
        await scenario.GivenAsync(aggregate => aggregate.HandleAsync(new DisableMediaControl(aggregate.Id,
            RequestId.New, "{}", "fingerprint", "grant-1",
            new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 30, 12, 1, 0, TimeSpan.Zero), new MediaIntentInputs()),
            new DisableMediaControlHandler()));
        await scenario.WhenCommittedAsync(aggregate => aggregate.HandleAsync(new CompleteMediaResult(aggregate.Id,
            RequestId.New, "device_acknowledged", "", aggregate.ConcurrencyToken!), new CompleteMediaResultHandler()));

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldSucceed();
        Assert.Equal("installation", scenario.Aggregate.State.DeviceKey);
        Assert.Equal(-1, scenario.Aggregate.State.Step);
    }

    /// <summary>Reads the exact persisted request state and token without requiring a projection.</summary>
    [Fact]
    public async Task Reader_returns_the_latest_durable_state_and_its_token()
    {
        InMemoryEventRepository repository = new();
        MediaOperationExecutor executor = new(repository, repository, repository, repository,
            new InMemoryLockRepository(), NullLogger<MediaOperationExecutor>.Instance);
        Guid operationId = Guid.NewGuid();
        DateTimeOffset accepted = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var result = await executor.RequestAsync(operationId, "volume", DEVICE_KEY, "{}", "fingerprint",
            "grant-1", accepted, accepted.AddMinutes(1), new MediaIntentInputs(Value: 37));
        result.ShouldSucceed();

        MediaOperationView? view = await new MediaOperationReader(repository).ReadAsync(operationId);
        Assert.NotNull(view);
        Assert.Equal("accepted", view.State.Status);
        Assert.Equal("volume", view.State.Action);
        Assert.Equal(-1, view.State.Step);
        Assert.Equal(1, view.EventSequence);
        Assert.Equal(result.CommitReceipt!.AggregateConcurrencyToken.ToString(), view.ConcurrencyToken);
    }

    /// <summary>An expired coordination claim after a dispatch marker remains auditable without a device send.</summary>
    [Fact]
    public async Task Dispatch_marker_may_record_expiry_before_external_release()
    {
        var scenario = Scenario();
        await scenario.GivenAsync(async aggregate =>
        {
            await aggregate.HandleAsync(Volume(aggregate, 37), new ChoosePlayerVolumeHandler());
            return await aggregate.HandleAsync(new StartMediaDispatch(aggregate.Id, RequestId.New,
                new DateTimeOffset(2026, 9, 30, 12, 0, 15, TimeSpan.Zero), 0, aggregate.ConcurrencyToken!),
                new StartMediaDispatchHandler());
        });
        await scenario.WhenCommittedAsync(aggregate => aggregate.HandleAsync(new CompleteMediaResult(aggregate.Id,
            RequestId.New, "expired", "media.operation.expired", aggregate.ConcurrencyToken!),
            new CompleteMediaResultHandler()));

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldSucceed();
        Assert.Equal("expired", scenario.Aggregate.State.Status);
        Assert.Equal(0, scenario.Aggregate.State.Step);
    }

    /// <summary>A later technical revocation or uncertainty remains auditable after a committed result.</summary>
    /// <param name="firstOutcome">Earlier persisted domain conclusion.</param>
    /// <param name="recoveredOutcome">Stricter coordinator recovery conclusion.</param>
    [Theory]
    [InlineData("rejected", "revoked")]
    [InlineData("rejected", "outcome_unknown")]
    [InlineData("expired", "revoked")]
    [InlineData("expired", "outcome_unknown")]
    [InlineData("revoked", "outcome_unknown")]
    [InlineData("desired_state_observed", "outcome_unknown")]
    public async Task Recovered_fence_can_follow_a_committed_result(string firstOutcome, string recoveredOutcome)
    {
        var scenario = Scenario();
        await scenario.GivenAsync(async aggregate =>
        {
            await aggregate.HandleAsync(Volume(aggregate, 37), new ChoosePlayerVolumeHandler());
            await aggregate.HandleAsync(new StartMediaDispatch(aggregate.Id, RequestId.New,
                new DateTimeOffset(2026, 9, 30, 12, 0, 15, TimeSpan.Zero), 0, aggregate.ConcurrencyToken!),
                new StartMediaDispatchHandler());
            return await aggregate.HandleAsync(new CompleteMediaResult(aggregate.Id, RequestId.New,
                firstOutcome, "media.first.result", aggregate.ConcurrencyToken!), new CompleteMediaResultHandler());
        });
        await scenario.WhenCommittedAsync(aggregate => aggregate.HandleAsync(new CompleteMediaResult(aggregate.Id,
            RequestId.New, recoveredOutcome, "media.executor.interrupted", aggregate.ConcurrencyToken!),
            new CompleteMediaResultHandler()));

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldSucceed();
        Assert.Equal(recoveredOutcome, scenario.Aggregate.State.Status);
        Assert.Equal("media.executor.interrupted", scenario.Aggregate.State.ResultCode);
        Assert.Equal(4, scenario.StoredEvents().Count());
    }

    /// <summary>Recovery cannot turn an uncertainty fence or an earlier refusal into success.</summary>
    /// <param name="firstOutcome">Earlier persisted domain conclusion.</param>
    /// <param name="attemptedOutcome">Unsafe proposed replacement.</param>
    [Theory]
    [InlineData("outcome_unknown", "revoked")]
    [InlineData("outcome_unknown", "desired_state_observed")]
    [InlineData("rejected", "desired_state_observed")]
    [InlineData("expired", "device_acknowledged")]
    public async Task Recovered_fence_never_becomes_a_success(string firstOutcome, string attemptedOutcome)
    {
        var scenario = Scenario();
        await scenario.GivenAsync(async aggregate =>
        {
            await aggregate.HandleAsync(Volume(aggregate, 37), new ChoosePlayerVolumeHandler());
            await aggregate.HandleAsync(new StartMediaDispatch(aggregate.Id, RequestId.New,
                new DateTimeOffset(2026, 9, 30, 12, 0, 15, TimeSpan.Zero), 0, aggregate.ConcurrencyToken!),
                new StartMediaDispatchHandler());
            return await aggregate.HandleAsync(new CompleteMediaResult(aggregate.Id, RequestId.New,
                firstOutcome, "media.first.result", aggregate.ConcurrencyToken!), new CompleteMediaResultHandler());
        });
        await scenario.WhenAsync(aggregate => aggregate.HandleAsync(new CompleteMediaResult(aggregate.Id,
            RequestId.New, attemptedOutcome, "media.unsafe.transition", aggregate.ConcurrencyToken!),
            new CompleteMediaResultHandler()));

        Assert.NotNull(scenario.Result);
        scenario.Result!.ShouldFailWith("media.result.invalid_phase");
        scenario.NewEvents().ShouldBeEmpty();
        Assert.Equal(firstOutcome, scenario.Aggregate.State.Status);
    }
}
