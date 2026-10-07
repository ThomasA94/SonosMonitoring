using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Devbone.Frameworks.Ddd.Results;
using SonosMonitoring.Application.MediaControl.Transport;
using SonosMonitoring.Domain.MediaControl;

namespace SonosMonitoring.Application.MediaControl;

/// <summary>Commits semantic intentions before admitting them to the authoritative device execution queue.</summary>
public sealed class MediaRequestService
{
    private readonly MediaOperationExecutor _executor;
    private readonly MediaOperationReader _reader;
    private readonly IMediaCoordinationStore _store;
    private readonly IMediaDeviceGateway _devices;
    private readonly int _lifetimeSeconds;

    /// <summary>Receives independent domain, technical coordination and device ports.</summary>
    /// <param name="executor">Event-sourced intention executor.</param>
    /// <param name="reader">Authoritative operation reader.</param>
    /// <param name="store">Durable permission, order and uncertainty coordination.</param>
    /// <param name="devices">Hardened local read and preparation gateway.</param>
    /// <param name="lifetimeSeconds">Dispatch validity period.</param>
    public MediaRequestService(MediaOperationExecutor executor, MediaOperationReader reader, IMediaCoordinationStore store,
        IMediaDeviceGateway devices, int lifetimeSeconds)
    {
        this._executor = executor;
        this._reader = reader;
        this._store = store;
        this._devices = devices;
        this._lifetimeSeconds = lifetimeSeconds;
    }

    /// <summary>Accepts an immutable named action; retries preserve the original stream, generation and admission position.</summary>
    /// <param name="id">Stable retry identity.</param>
    /// <param name="payload">Originally displayed context and intention fields.</param>
    /// <param name="ct">HTTP request cancellation before durable acceptance.</param>
    /// <returns>Confirmed operation stream state.</returns>
    public async Task<MediaOperationView> AcceptAsync(Guid id, MediaExecutionPayload payload, CancellationToken ct)
    {
        if (payload.Context is null || payload.Context.MemberDeviceKeys is null ||
            payload.Context.MemberDeviceKeys.Count is < 1 or > 256 || payload.DeviceKey != payload.Context.DeviceKey)
        {
            throw new MediaCoordinationException("media.context.invalid", 400);
        }
        payload = payload with { Context = payload.Context with { MemberDeviceKeys = payload.Context.MemberDeviceKeys.Order(StringComparer.Ordinal).ToArray() } };
        if (payload.Inputs.DestinationContext is { } destinationContext &&
            (payload.Action != "join" || destinationContext.DeviceKey != payload.Inputs.Reference ||
             destinationContext.MemberDeviceKeys is null || destinationContext.MemberDeviceKeys.Count is < 1 or > 256))
        {
            throw new MediaCoordinationException("media.context.invalid", 400);
        }
        string json = JsonSerializer.Serialize(payload);
        string fingerprint = Hash(json);
        var previous = await this._reader.ReadAsync(id, ct);
        if (previous is not null && previous.State.Fingerprint != fingerprint)
        {
            throw new MediaCoordinationException("media.idempotency.conflict");
        }
        if (previous is not null)
        {
            var existing = await this._store.FindAsync(id, ct);
            if (existing is not null)
            {
                return previous;
            }
        }
        Ensure(MediaControlOperation.ValidateInputs(payload.Action, ConvertInputs(payload.Inputs)));
        var grant = await this._store.StatusAsync(ct);
        if (grant.Enabled == false)
        {
            throw new MediaCoordinationException("media.control.disabled", 403);
        }
        if (previous is not null)
        {
            payload = JsonSerializer.Deserialize<MediaExecutionPayload>(previous.State.PayloadJson)
                ?? throw new MediaCoordinationException("media.context.invalid", 400);
        }
        else if (payload.Action == "join" && payload.Inputs.DestinationContext is null)
        {
            var destination = await this._devices.ReadAsync(payload.Inputs.Reference!, ct);
            payload = payload with { Inputs = payload.Inputs with { DestinationContext = destination.Context } };
        }
        // Preparation performs only bounded reads and validates source-specific support before admission.
        await this._devices.PrepareAsync(payload.DeviceKey, payload.Action, payload.Context, payload.Inputs, ct);
        string[] targets = payload.Action is "volume" or "mute" or "unmute"
            ? [payload.DeviceKey] : payload.Context.MemberDeviceKeys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (payload.Action == "join")
        {
            var destination = payload.Inputs.DestinationContext!;
            if (destination.HouseholdId != payload.Context.HouseholdId)
            {
                throw new MediaCoordinationException("media.household.mismatch");
            }
            targets = targets.Concat(destination.MemberDeviceKeys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        }
        if (previous is null)
        {
            payload = payload with { TargetEpochs = await this._store.EpochsAsync(targets, ct) };
            DateTimeOffset accepted = DateTimeOffset.UtcNow;
            json = JsonSerializer.Serialize(payload);
            Ensure(await this._executor.RequestAsync(id, payload.Action, payload.DeviceKey, json, fingerprint, grant.Generation,
                accepted, accepted.AddSeconds(this._lifetimeSeconds), ConvertInputs(payload.Inputs), ct));
            previous = await this._reader.ReadAsync(id, ct) ?? throw new MediaCoordinationException("media.commit.unavailable", 503);
        }
        var state = previous.State;
        // A concurrent identical retry may have committed a different server snapshot first.
        // Always reserve the conflict targets from the winning immutable event, never this request's local reads.
        var original = JsonSerializer.Deserialize<MediaExecutionPayload>(state.PayloadJson)
            ?? throw new MediaCoordinationException("media.context.invalid", 400);
        targets = original.Action is "volume" or "mute" or "unmute" ? [original.DeviceKey] :
            original.Context.MemberDeviceKeys.Concat(original.Inputs.DestinationContext?.MemberDeviceKeys ?? [])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        await this._store.AdmitAsync(new(id, 0, state.Fingerprint, state.Generation, targets, state.PayloadJson,
            state.AcceptedAtUtc, state.ExpiresAtUtc), ct);
        return previous;
    }

    /// <summary>Persists an operator decision before changing technical permission or risk fences.</summary>
    /// <param name="id">Stable operator decision identity.</param>
    /// <param name="action">Enable, disable or explicit risk acceptance.</param>
    /// <param name="riskOperations">Exact uncertain operation identities.</param>
    /// <param name="ct">Bounded request cancellation.</param>
    /// <returns>Authoritative permission after the decision.</returns>
    public async Task<MediaGrantStatus> DecideAsync(Guid id, string action, Guid[] riskOperations, CancellationToken ct)
    {
        string descriptor = JsonSerializer.Serialize(new { Action = action, Operations = riskOperations.Order().ToArray() });
        string fingerprint = Hash(descriptor);
        var previous = await this._reader.ReadAsync(id, ct);
        if (previous is not null && previous.State.Fingerprint != fingerprint)
        {
            throw new MediaCoordinationException("media.idempotency.conflict");
        }
        var grant = await this._store.StatusAsync(ct);
        var inputs = action == "accept-risk" ? new MediaIntentInputs(Reference: Hash(string.Join(',', riskOperations.Order()))) : new MediaIntentInputs();
        if (previous is null)
        {
            DateTimeOffset accepted = DateTimeOffset.UtcNow;
            Ensure(await this._executor.RequestAsync(id, action, "installation", descriptor, fingerprint, grant.Generation,
                accepted, accepted.AddSeconds(this._lifetimeSeconds), inputs, ct));
            previous = await this._reader.ReadAsync(id, ct) ?? throw new MediaCoordinationException("media.commit.unavailable", 503);
        }
        var outcome = action switch
        {
            "enable-control" => await this._store.SetGrantAsync(true, id, previous.State.Generation, ct),
            "disable-control" => await this._store.SetGrantAsync(false, id, previous.State.Generation, ct),
            "accept-risk" => await this._store.AcceptRiskAsync(riskOperations, id, ct),
            _ => throw new MediaCoordinationException("media.action.unsupported", 400),
        };
        var current = (await this._reader.ReadAsync(id, ct))!;
        if (current.State.Status == "accepted")
        {
            Ensure(await this._executor.CompleteAsync(id, current.ConcurrencyToken, DeriveRequestId(id, "decision-result"),
                "device_acknowledged", "media.decision.recorded", ct));
        }
        return outcome;
    }

    /// <summary>Converts transport fields to the domain's own independent inputs.</summary>
    /// <param name="inputs">Structured transport fields.</param>
    /// <returns>Domain validation inputs.</returns>
    public static MediaIntentInputs ConvertInputs(MediaWriteInputs inputs) =>
        new(inputs.Value, inputs.PositionSeconds, inputs.Reference, inputs.Index, inputs.NewIndex, inputs.Mode);

    /// <summary>Rejects unsuccessful commands with stable domain codes.</summary>
    /// <param name="result">Actual command or validation result.</param>
    public static void Ensure(Devbone.Frameworks.Ddd.Results.IResult result)
    {
        if (result.Success == false)
        {
            var error = result.Errors.First();
            throw new MediaCoordinationException(error.Code, error.Category == DomainErrorCategory.Validation ? 400 : 409);
        }
    }

    /// <summary>Derives a stable internal request identity without reusing the user's initial command key.</summary>
    /// <param name="operation">Original operation identity.</param>
    /// <param name="phase">Immutable internal transition name.</param>
    /// <returns>Deterministic transition identity.</returns>
    public static Guid DeriveRequestId(Guid operation, string phase) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(operation.ToString("D") + ":" + phase)).AsSpan(0, 16));

    /// <summary>Hashes a descriptor without persisting credential-bearing raw values in diagnostics.</summary>
    /// <param name="value">Canonical descriptor.</param>
    /// <returns>SHA-256 fingerprint.</returns>
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
