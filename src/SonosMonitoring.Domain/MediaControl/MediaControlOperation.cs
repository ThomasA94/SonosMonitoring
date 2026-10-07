using System.Text.Json;
using System.Text.RegularExpressions;
using SonosMonitoring.Domain.MediaControl.Events;
using SonosMonitoring.Platform;

namespace SonosMonitoring.Domain.MediaControl;

/// <summary>Owns one bounded media intention and its durable dispatch lifecycle.</summary>
[AggregateName(MediaControlNames.AGGREGATE)]
public sealed partial class MediaControlOperation : AggregateRoot<MediaControlOperation, MediaOperationState, MediaOperationId>
{
    /// <summary>Creates an empty operation for rehydration.</summary>
    /// <param name="id">Stable stream identity.</param>
    /// <param name="changeTracker">Framework transaction tracker.</param>
    public MediaControlOperation(MediaOperationId id, IChangeTracker<MediaOperationId> changeTracker) : base(id, changeTracker)
    {
        this.ReplaceAggregateState(new MediaOperationState(id));
    }

    /// <summary>Gets the rehydrated operation state.</summary>
    public MediaOperationState State => this.AggregateState;

    /// <summary>Validates the immutable envelope and action-specific structured inputs.</summary>
    /// <param name="command">Named user intention.</param>
    public static IResult ValidateRequest(RequestMediaControl command)
    {
        bool installationDecision = command.Action is "enable-control" or "disable-control" or "accept-risk";
        if ((installationDecision && command.DeviceKey != "installation") ||
            (installationDecision == false && (string.IsNullOrEmpty(command.DeviceKey) || command.DeviceKey.Length > 128 ||
            Regex.IsMatch(command.DeviceKey, @"\ARINCON_[A-Z0-9_]+\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) == false)))
        {
            return Error("media.device.invalid", "A canonical player identity is required.");
        }
        if (string.IsNullOrWhiteSpace(command.Fingerprint) || command.Fingerprint.Length > 128 ||
            string.IsNullOrWhiteSpace(command.Generation) || command.Generation.Length > 128)
        {
            return Error("media.context.invalid", "A fingerprint and grant generation are required.");
        }
        if (command.AcceptedAtUtc.Offset != TimeSpan.Zero || command.ExpiresAtUtc.Offset != TimeSpan.Zero ||
            command.ExpiresAtUtc <= command.AcceptedAtUtc)
        {
            return Error("media.deadline.invalid", "A future UTC dispatch deadline is required.");
        }
        if (command.PayloadJson.Length is < 2 or > 16384)
        {
            return Error("media.context.invalid", "The execution context is invalid.");
        }
        try
        {
            using JsonDocument context = JsonDocument.Parse(command.PayloadJson);
            if (context.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Error("media.context.invalid", "The execution context must be an object.");
            }
        }
        catch (JsonException)
        {
            return Error("media.context.invalid", "The execution context is not valid JSON.");
        }
        return ValidateInputs(command.Action, command.Inputs);
    }

    /// <summary>Validates the type and range of fields for the named intention.</summary>
    /// <param name="action">Named user intention.</param>
    /// <param name="inputs">Structured command fields.</param>
    public static IResult ValidateInputs(string action, MediaIntentInputs inputs)
    {
        if (inputs is null)
        {
            return Error("media.input.invalid", "Action inputs are required.");
        }
        bool any = inputs.Value.HasValue || inputs.PositionSeconds.HasValue || inputs.Reference is not null ||
            inputs.Index.HasValue || inputs.NewIndex.HasValue || inputs.Mode is not null;
        bool valid = action switch
        {
            "resume" or "pause" or "stop" or "mute" or "unmute" or "next" or "previous" or
                "group-mute" or "group-unmute" or "leave" or "queue-clear" or
                "enable-control" or "disable-control" => any == false,
            "accept-risk" => inputs.Reference is { Length: 64 } &&
                Regex.IsMatch(inputs.Reference, @"\A[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) &&
                inputs.Value is null &&
                inputs.PositionSeconds is null && inputs.Index is null && inputs.NewIndex is null && inputs.Mode is null,
            "volume" or "group-volume" => inputs.Value is >= 0 and <= 100 &&
                inputs.PositionSeconds is null && inputs.Reference is null && inputs.Index is null &&
                inputs.NewIndex is null && inputs.Mode is null,
            "crossfade" => inputs.Value is 0 or 1 && inputs.PositionSeconds is null &&
                inputs.Reference is null && inputs.Index is null && inputs.NewIndex is null && inputs.Mode is null,
            "seek" => inputs.PositionSeconds is >= 0 and <= 86399 &&
                double.IsFinite(inputs.PositionSeconds.Value) && inputs.Value is null &&
                inputs.Reference is null && inputs.Index is null && inputs.NewIndex is null && inputs.Mode is null,
            "mode" => inputs.Mode is "normal" or "repeat-all" or "repeat-one" or "shuffle" or "shuffle-repeat" &&
                inputs.Value is null && inputs.PositionSeconds is null && inputs.Reference is null &&
                inputs.Index is null && inputs.NewIndex is null,
            "start-favorite" or "start-stream" or "queue-play" or "queue-append" or "join" =>
                IsReference(inputs.Reference) && inputs.Value is null && inputs.PositionSeconds is null &&
                inputs.Index is null && inputs.NewIndex is null && inputs.Mode is null,
            "queue-remove" => inputs.Index is >= 1 && inputs.Value is null &&
                inputs.PositionSeconds is null && inputs.Reference is null && inputs.NewIndex is null && inputs.Mode is null,
            "queue-reorder" => inputs.Index is >= 1 && inputs.NewIndex is >= 1 &&
                inputs.Value is null && inputs.PositionSeconds is null && inputs.Reference is null && inputs.Mode is null,
            _ => false,
        };
        return valid ? new Result() : Error("media.input.invalid", "Inputs do not match the named media action.");
    }

    /// <summary>Allows creation only on an empty operation stream.</summary>
    public IResult ValidateNew() => this.State.Status.Length == 0
        ? new Result() : Conflict("media.idempotency.conflict", "The operation already exists.");

    /// <summary>Allows one dispatch per step before the captured deadline.</summary>
    /// <param name="command">Requested writing step.</param>
    public IResult ValidateStart(StartMediaDispatch command)
    {
        if (command.StartedAtUtc.Offset != TimeSpan.Zero || command.StartedAtUtc < this.State.AcceptedAtUtc ||
            command.StartedAtUtc > this.State.ExpiresAtUtc)
        {
            return Conflict("media.operation.expired", "The dispatch deadline has passed.");
        }
        if (this.State.Action is "enable-control" or "disable-control" or "accept-risk")
        {
            return Conflict("media.dispatch.invalid", "Installation decisions have no device dispatch step.");
        }
        bool firstStep = this.State.Status == "accepted" && command.Step == 0;
        bool nextStep = this.State.Status == "device_acknowledged" && command.Step == this.State.Step + 1;
        return firstStep || nextStep ? new Result()
            : Conflict("media.dispatch.invalid", "The writing step has already begun or is out of order.");
    }

    /// <summary>Checks that a result is valid for the current durable phase.</summary>
    /// <param name="command">Result to record.</param>
    public IResult ValidateComplete(CompleteMediaResult command)
    {
        bool known = command.Outcome is "device_acknowledged" or "desired_state_observed" or "rejected" or
            "expired" or "revoked" or "outcome_unknown";
        if (known == false || command.ResultCode.Length > 128)
        {
            return Error("media.result.invalid", "The result classification is invalid.");
        }
        bool controlDecision = this.State.Action is "enable-control" or "disable-control" or "accept-risk";
        bool preDispatch = this.State.Status == "accepted" &&
            (command.Outcome is "rejected" or "expired" or "revoked" ||
            (controlDecision && command.Outcome == "device_acknowledged"));
        bool postDispatch = this.State.Status == "dispatch_started" && command.Outcome is
            "device_acknowledged" or "desired_state_observed" or "rejected" or "revoked" or "expired" or "outcome_unknown";
        bool afterStep = this.State.Status == "device_acknowledged" && command.Outcome is
            "outcome_unknown" or "revoked" or "expired";
        // A later coordinator fact can be stricter than a result committed before its own write failed.
        // The earlier fact remains in the event history; uncertainty must never become success again.
        bool terminalReconciliation = this.State.Status switch
        {
            "rejected" or "expired" => command.Outcome is "revoked" or "outcome_unknown",
            "revoked" or "desired_state_observed" => command.Outcome == "outcome_unknown",
            _ => false,
        };
        return preDispatch || postDispatch || afterStep || terminalReconciliation ? new Result()
            : Conflict("media.result.invalid_phase", "The result does not match the current operation phase.");
    }

    /// <summary>Records the accepted user intention without contacting the device.</summary>
    /// <param name="command">Validated named intention.</param>
    public IAggregateModificationResult Request(RequestMediaControl command) => this.Emit(command.RequestId, new MediaControlRequested
    {
        AggregateId = this.Id,
        Action = command.Action,
        DeviceKey = command.DeviceKey,
        PayloadJson = command.PayloadJson,
        Fingerprint = command.Fingerprint,
        Generation = command.Generation,
        AcceptedAtUtc = command.AcceptedAtUtc,
        ExpiresAtUtc = command.ExpiresAtUtc,
        Status = "accepted",
        Step = -1,
    });

    /// <summary>Records an irreversible writing step before its external send attempt.</summary>
    /// <param name="command">Validated dispatch transition.</param>
    public IAggregateModificationResult Start(StartMediaDispatch command) => this.Emit(command.RequestId, new MediaControlDispatchStarted
    {
        AggregateId = this.Id,
        Action = this.State.Action,
        DeviceKey = this.State.DeviceKey,
        PayloadJson = this.State.PayloadJson,
        Fingerprint = this.State.Fingerprint,
        Generation = this.State.Generation,
        AcceptedAtUtc = this.State.AcceptedAtUtc,
        ExpiresAtUtc = this.State.ExpiresAtUtc,
        Status = "dispatch_started",
        Step = command.Step,
    });

    /// <summary>Records the observed result without inferring device causality.</summary>
    /// <param name="command">Validated result transition.</param>
    public IAggregateModificationResult Complete(CompleteMediaResult command) => this.Emit(command.RequestId, new MediaControlResultRecorded
    {
        AggregateId = this.Id,
        Action = this.State.Action,
        DeviceKey = this.State.DeviceKey,
        PayloadJson = this.State.PayloadJson,
        Fingerprint = this.State.Fingerprint,
        Generation = this.State.Generation,
        AcceptedAtUtc = this.State.AcceptedAtUtc,
        ExpiresAtUtc = this.State.ExpiresAtUtc,
        Status = command.Outcome,
        Step = this.State.Step,
        Outcome = command.Outcome,
        ResultCode = command.ResultCode,
    });

    /// <summary>Rehydrates accepted immutable facts.</summary>
    /// <param name="e">Stored acceptance fact.</param>
    private void Apply(MediaControlRequested e) => this.ReplaceAggregateState(new MediaOperationState(this.Id,
        e.Action, e.DeviceKey, e.PayloadJson, e.Fingerprint, e.Generation, e.AcceptedAtUtc, e.ExpiresAtUtc,
        e.Status, e.Step, e.Outcome, e.ResultCode));

    /// <summary>Rehydrates the last irrevocable dispatch beginning.</summary>
    /// <param name="e">Stored dispatch fact.</param>
    private void Apply(MediaControlDispatchStarted e) => this.ReplaceAggregateState(new MediaOperationState(this.Id,
        e.Action, e.DeviceKey, e.PayloadJson, e.Fingerprint, e.Generation, e.AcceptedAtUtc, e.ExpiresAtUtc,
        e.Status, e.Step, e.Outcome, e.ResultCode));

    /// <summary>Rehydrates the persisted outcome.</summary>
    /// <param name="e">Stored result fact.</param>
    private void Apply(MediaControlResultRecorded e) => this.ReplaceAggregateState(new MediaOperationState(this.Id,
        e.Action, e.DeviceKey, e.PayloadJson, e.Fingerprint, e.Generation, e.AcceptedAtUtc, e.ExpiresAtUtc,
        e.Status, e.Step, e.Outcome, e.ResultCode));

    /// <summary>Checks that a reference is bounded and nonblank.</summary>
    /// <param name="reference">Stable source, queue, or group reference.</param>
    private static bool IsReference(string? reference) => string.IsNullOrWhiteSpace(reference) == false && reference.Length <= 1024;

    /// <summary>Creates a stable input validation failure.</summary>
    /// <param name="code">Machine-readable error code.</param>
    /// <param name="message">Safe diagnostic text.</param>
    private static IResult Error(string code, string message) => new Result(new DomainError(code, message, DomainErrorCategory.Validation));

    /// <summary>Creates a state-dependent conflict.</summary>
    /// <param name="code">Machine-readable error code.</param>
    /// <param name="message">Safe diagnostic text.</param>
    private static IResult Conflict(string code, string message) => new Result(new DomainError(code, message, DomainErrorCategory.Conflict));
}
