using System.Text.Json;

namespace SonosMonitoring.Application.MediaControl;

/// <summary>Closes committed intentions that can no longer enter the execution queue without ever dispatching them.</summary>
public sealed class MediaAdmissionReconciler
{
    private readonly MediaOperationReader _reader;
    private readonly IMediaCoordinationStore _store;

    /// <summary>Uses authoritative events and atomic coordination to queue a terminal audit fact.</summary>
    /// <param name="reader">Authoritative operation history.</param>
    /// <param name="store">Admission and terminalization fence.</param>
    public MediaAdmissionReconciler(MediaOperationReader reader, IMediaCoordinationStore store)
    {
        this._reader = reader;
        this._store = store;
    }

    /// <summary>Reconciles a candidate against current authorities; stale projection hints cannot authorize a write.</summary>
    /// <param name="id">Original immutable operation identity.</param>
    /// <param name="ct">Bounded cancellation; a durable terminal fence survives interrupted audit writes.</param>
    /// <returns>The latest retained semantic state, or null for a nonexistent operation.</returns>
    public async Task<MediaOperationView?> ReconcileAsync(Guid id, CancellationToken ct)
    {
        var view = await this._reader.ReadAsync(id, ct);
        if (view is null || view.State.Status != "accepted" || view.State.DeviceKey == "installation")
        {
            return view;
        }
        var coordination = await this._store.FindAsync(id, ct);
        if (coordination is null)
        {
            var state = view.State;
            var payload = JsonSerializer.Deserialize<MediaExecutionPayload>(state.PayloadJson)
                ?? throw new JsonException("Missing media admission descriptor.");
            string[] targets = payload.Action is "volume" or "mute" or "unmute" ? [payload.DeviceKey] :
                payload.Context.MemberDeviceKeys.Concat(payload.Inputs.DestinationContext?.MemberDeviceKeys ?? [])
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            await this._store.CloseAdmissionAsync(new(id, 0, state.Fingerprint, state.Generation,
                targets, state.PayloadJson, state.AcceptedAtUtc, state.ExpiresAtUtc), ct);
        }
        // The durable unrecorded outcome is synchronized by the executor worker. Query and retry callers
        // must not race its dispatch/result commands with an independent concurrency-token transition.
        return view;
    }
}
