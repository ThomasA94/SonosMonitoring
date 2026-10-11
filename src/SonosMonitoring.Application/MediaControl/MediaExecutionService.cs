using System.Text.Json;
using SonosMonitoring.Application.MediaControl.Transport;

namespace SonosMonitoring.Application.MediaControl;

/// <summary>Executes one admitted intention with persisted per-step claims and bounded reconciliation.</summary>
public sealed class MediaExecutionService
{
    private readonly MediaOperationExecutor _executor;
    private readonly MediaOperationReader _reader;
    private readonly IMediaCoordinationStore _store;
    private readonly IMediaDeviceGateway _devices;

    /// <summary>Receives independent execution and persistence ports.</summary>
    /// <param name="executor">Semantic operation transitions.</param>
    /// <param name="reader">Authoritative operation history.</param>
    /// <param name="store">Technical order and dispatch fences.</param>
    /// <param name="devices">Hardened device gateway.</param>
    public MediaExecutionService(MediaOperationExecutor executor, MediaOperationReader reader, IMediaCoordinationStore store, IMediaDeviceGateway devices)
    {
        this._executor = executor;
        this._reader = reader;
        this._store = store;
        this._devices = devices;
    }

    /// <summary>Retains recovery and revocation outcomes without performing device reads or writes.</summary>
    /// <param name="operation">Authoritative technical outcome awaiting semantic audit.</param>
    /// <param name="ct">Independent audit budget.</param>
    public async Task SynchronizeAsync(CoordinatedMediaOperation operation, CancellationToken ct)
    {
        var current = await this._reader.ReadAsync(operation.OperationId, ct);
        if (current is null)
        {
            throw new MediaCoordinationException("media.commit.unavailable", 503);
        }
        string status = operation.Status;
        if (current.State.Status != status)
        {
            MediaRequestService.Ensure(await this._executor.CompleteAsync(operation.OperationId, current.ConcurrencyToken,
                MediaRequestService.DeriveRequestId(operation.OperationId, "audit-" + status), status,
                operation.Code ?? "media.executor.interrupted", ct));
        }
        await this._store.MarkRecordedAsync(operation.OperationId, operation.Status, ct);
    }

    /// <summary>Never repeats a released step after restart, timeout, cancellation or ambiguous commit.</summary>
    /// <param name="operation">Original durable admission.</param>
    /// <param name="ct">Entire device execution deadline.</param>
    public async Task ProcessAsync(CoordinatedMediaOperation operation, CancellationToken ct)
    {
        if (operation.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            await this.RecordAsync(operation.OperationId, "expired", "media.operation.expired");
            return;
        }
        if (await this._store.IsReadyAsync(operation, ct) == false)
        {
            return;
        }
        bool released = false;
        int? lastAcknowledgedStep = null;
        int? currentStep = null;
        try
        {
            var persisted = await this._reader.ReadAsync(operation.OperationId, ct)
                ?? throw new MediaCoordinationException("media.commit.unavailable", 503);
            if (persisted.State.Status != "accepted")
            {
                if (persisted.State.Status is "rejected" or "expired" or "revoked" or "outcome_unknown")
                {
                    await this._store.FinishAsync(operation.OperationId, persisted.State.Status, persisted.State.ResultCode, ct);
                    await this._store.MarkRecordedAsync(operation.OperationId, persisted.State.Status, ct);
                }
                else
                {
                    await this.RecordAsync(operation.OperationId, "outcome_unknown", "media.executor.interrupted");
                }
                return;
            }
            var payload = JsonSerializer.Deserialize<MediaExecutionPayload>(operation.PayloadJson)
                ?? throw new MediaCoordinationException("media.context.invalid", 400);
            var steps = await this._devices.PrepareAsync(payload.DeviceKey, payload.Action, payload.Context, payload.Inputs, ct);
            if (steps.Count is < 1 or > 8)
            {
                throw new MediaCoordinationException("media.steps.invalid", 400);
            }
            for (int index = 0; index < steps.Count; index++)
            {
                var state = (await this._reader.ReadAsync(operation.OperationId, ct))!;
                MediaRequestService.Ensure(await this._executor.StartAsync(operation.OperationId, state.ConcurrencyToken,
                    MediaRequestService.DeriveRequestId(operation.OperationId, "dispatch-" + index), index, ct));
                if (await this._store.ReleaseAsync(operation.OperationId, index, ct) == false)
                {
                    var current = await this._store.FindAsync(operation.OperationId, ct);
                    string status = current?.Status is "outcome_unknown" or "expired" ? current.Status : "revoked";
                    await this.RecordAsync(operation.OperationId, status, released ? "media.partial.revoked" : "media.dispatch.not_released");
                    return;
                }
                released = true;
                currentStep = index;
                await this._devices.SendAsync(steps[index], ct);
                lastAcknowledgedStep = index;
                currentStep = null;
                if (index + 1 < steps.Count)
                {
                    await this.RecordAsync(operation.OperationId, "device_acknowledged", "media.step.acknowledged", index);
                }
            }
            bool? observed = await this._devices.ConfirmAsync(payload.Action, payload.Context, payload.Inputs, ct);
            await this.RecordAsync(operation.OperationId, observed == true ? "desired_state_observed" : "device_acknowledged",
                observed == true ? "media.desired_state.observed" : "media.device.acknowledged", final: true);
        }
        catch (Exception exception) when (exception is MediaCoordinationException or MediaDeviceException or
            HttpRequestException or IOException or OperationCanceledException or FormatException or JsonException)
        {
            bool definite = exception is MediaDeviceException device && device.DefiniteRejection;
            bool partialRejection = definite && lastAcknowledgedStep is not null && currentStep is not null;
            string code = exception switch
            {
                MediaCoordinationException coordination => coordination.Code,
                MediaDeviceException transport => "media." + transport.Code,
                _ => released ? "media.outcome.unknown" : "media.device.unreachable",
            };
            await this.RecordAsync(operation.OperationId, released && definite == false ? "outcome_unknown" : "rejected",
                partialRejection ? "media.partial.rejected" : code, lastAcknowledgedStep);
        }
    }

    /// <summary>Commits the domain result before clearing technical fences, with an independent bounded budget.</summary>
    /// <param name="id">Original operation.</param>
    /// <param name="status">Semantic execution result.</param>
    /// <param name="code">Safe result diagnostic.</param>
    /// <param name="step">Optional acknowledged intermediate step.</param>
    /// <param name="final">Whether acknowledgement finishes the entire operation.</param>
    private async Task RecordAsync(Guid id, string status, string code, int? step = null, bool final = false)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var state = await this._reader.ReadAsync(id, budget.Token);
        if (state is not null)
        {
            MediaRequestService.Ensure(await this._executor.CompleteAsync(id, state.ConcurrencyToken,
                MediaRequestService.DeriveRequestId(id, "result-" + status + "-" + (step?.ToString() ?? "final")), status, code, budget.Token));
        }
        await this._store.FinishAsync(id, final && status == "device_acknowledged" ? "completed" : status, code, budget.Token);
        var recorded = await this._store.FindAsync(id, budget.Token);
        if (recorded?.Status == status || (final && recorded?.Status == "completed"))
        {
            await this._store.MarkRecordedAsync(id, recorded.Status, budget.Token);
        }
    }
}
