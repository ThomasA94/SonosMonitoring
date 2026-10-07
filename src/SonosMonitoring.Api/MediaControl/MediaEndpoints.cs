using System.Text.Json;
using Npgsql;
using SonosMonitoring.Application.MediaControl;
using SonosMonitoring.Application.MediaControl.Transport;
using SonosMonitoring.Contracts.MediaControl;

namespace SonosMonitoring.Api.MediaControl;

/// <summary>Exposes local media intentions, live observations and independent durable operation receipts.</summary>
public static class MediaEndpoints
{
    /// <summary>Maps endpoints protected by the installation's existing access middleware.</summary>
    /// <param name="app">API composition.</param>
    public static void MapMediaControl(this WebApplication app)
    {
        var group = app.MapGroup("/api/media");
        group.MapGet("/control", (Func<HttpContext, Task<IResult>>)(context => SafeAsync(context, StatusAsync)));
        group.MapPost("/control/enable", (Func<HttpContext, Task<IResult>>)(context => SafeAsync(context, value => DecideAsync(value, "enable-control", []))));
        group.MapPost("/control/disable", (Func<HttpContext, Task<IResult>>)(context => SafeAsync(context, value => DecideAsync(value, "disable-control", []))));
        group.MapPost("/control/accept-risk", (AcceptMediaRiskRequest request, HttpContext context) => SafeAsync(context,
            value => request.ConfirmDelayedEffect && request.OperationIds is not null
                ? DecideAsync(value, "accept-risk", request.OperationIds.ToArray())
                : Task.FromResult(Problem("media.risk.confirmation.required", 400))));
        group.MapGet("/players/{deviceKey}/state", (string deviceKey, HttpContext context) => SafeAsync(context,
            async value => Results.Ok(Map(await Devices(value).ReadAsync(deviceKey, value.RequestAborted)))));
        group.MapGet("/players/{deviceKey}/queue", (string deviceKey, int? offset, int? limit, HttpContext context) =>
            SafeAsync(context, value => BrowseAsync(value, deviceKey, "queue", offset, limit)));
        group.MapGet("/players/{deviceKey}/favorites", (string deviceKey, int? offset, int? limit, HttpContext context) =>
            SafeAsync(context, value => BrowseAsync(value, deviceKey, "favorites", offset, limit)));
        group.MapPost("/players/{deviceKey}/{action}", (string deviceKey, string action, MediaActionRequest request, HttpContext context) =>
            SafeAsync(context, value => AcceptAsync(value, deviceKey, action, request)));
        group.MapGet("/operations/{id:guid}", (Guid id, long? minSequence, HttpContext context) =>
            SafeAsync(context, value => FindAsync(value, id, minSequence)));
    }

    /// <summary>Reads durable permission and outstanding uncertainty fences.</summary>
    /// <param name="context">Request services and cancellation.</param>
    /// <returns>Safe control status.</returns>
    private static async Task<IResult> StatusAsync(HttpContext context) =>
        Results.Ok(Map(await Store(context).StatusAsync(context.RequestAborted)));

    /// <summary>Records a retryable installation decision before changing execution permission.</summary>
    /// <param name="context">Headers and bounded services.</param>
    /// <param name="action">Named decision.</param>
    /// <param name="ids">Exact uncertain operations acknowledged by the operator.</param>
    /// <returns>Updated durable grant status.</returns>
    private static async Task<IResult> DecideAsync(HttpContext context, string action, Guid[] ids) =>
        Results.Ok(Map(await Required<MediaRequestService>(context).DecideAsync(Key(context), action, ids, context.RequestAborted)));

    /// <summary>Browses bounded queue or favorite pages without exposing device URIs or credentials.</summary>
    /// <param name="context">Request services.</param>
    /// <param name="device">Stable UDN.</param>
    /// <param name="kind">Known browse surface.</param>
    /// <param name="offset">Zero-based browse offset.</param>
    /// <param name="limit">Maximum returned items.</param>
    /// <returns>Detached safe page.</returns>
    private static async Task<IResult> BrowseAsync(HttpContext context, string device, string kind, int? offset, int? limit)
    {
        if (offset is < 0 or > 10_000 || limit is < 1 or > 100)
        {
            return Problem("media.browse.invalid", 400);
        }
        var page = await Devices(context).BrowseAsync(device, kind, offset ?? 0, limit ?? 25, context.RequestAborted);
        return Results.Ok(new MediaPageDto(page.Items.Select(item => new MediaItemDto(item.Reference, item.Title,
            item.Artist, item.Album, item.Index)).ToArray(), page.Offset, page.Total, page.Revision));
    }

    /// <summary>Accepts one immutable intention only after both its event and durable admission exist.</summary>
    /// <param name="context">Headers and scoped command executor.</param>
    /// <param name="device">Selected physical player.</param>
    /// <param name="action">Explicit named intention.</param>
    /// <param name="request">Displayed context and structured inputs.</param>
    /// <returns>A durable receipt and bounded projection polling location.</returns>
    private static async Task<IResult> AcceptAsync(HttpContext context, string device, string action, MediaActionRequest request)
    {
        if (request.Context is null || action is "enable-control" or "disable-control" or "accept-risk")
        {
            return Problem("media.context.invalid", 400);
        }
        Guid id = Key(context);
        var displayed = request.Context;
        var payload = new MediaExecutionPayload(action, device, new MediaTargetContext(displayed.DeviceKey,
            displayed.HouseholdId, displayed.CoordinatorDeviceKey, displayed.MemberDeviceKeys,
            displayed.SourceToken, displayed.QueueRevision), new MediaWriteInputs(request.Value,
            request.PositionSeconds, request.Reference, request.Index, request.NewIndex, request.Mode,
            request.DestinationContext is { } destination ? new(destination.DeviceKey, destination.HouseholdId,
                destination.CoordinatorDeviceKey, destination.MemberDeviceKeys, destination.SourceToken, destination.QueueRevision) : null));
        var view = await Required<MediaRequestService>(context).AcceptAsync(id, payload, context.RequestAborted);
        var admission = await Store(context).FindAsync(id, context.RequestAborted);
        return Results.Accepted($"/api/media/operations/{id:D}?minSequence={view.EventSequence}", Map(id, view, admission));
    }

    /// <summary>Waits for merged event visibility while preserving authoritative execution fences in the receipt.</summary>
    /// <param name="context">Query services.</param>
    /// <param name="id">Original immutable operation identity.</param>
    /// <param name="minSequence">Optional confirmed commit watermark.</param>
    /// <returns>The operation outcome, or explicit pending visibility.</returns>
    private static async Task<IResult> FindAsync(HttpContext context, Guid id, long? minSequence)
    {
        if (id == Guid.Empty || minSequence is < 0)
        {
            return Problem("media.query.invalid", 400);
        }
        var view = await Required<MediaOperationReader>(context).ReadAsync(id, context.RequestAborted);
        if (view is null)
        {
            return Problem("media.operation.not_found", 404);
        }
        long? sequence = await Required<IMediaOperationProjection>(context).SequenceAsync(id, context.RequestAborted);
        if ((sequence ?? 0) < Math.Max(minSequence ?? 0, view.EventSequence))
        {
            context.Response.Headers.RetryAfter = "1";
            return Problem("projection.pending", 202);
        }
        var admission = await Store(context).FindAsync(id, context.RequestAborted);
        if (admission is null && view.State.Status == "accepted" && view.State.DeviceKey != "installation")
        {
            context.Response.Headers.RetryAfter = "1";
            return Problem("media.admission.pending", 202);
        }
        return Results.Ok(Map(id, view, admission));
    }

    /// <summary>Converts independently owned execution models into HTTP contracts.</summary>
    /// <param name="playback">Fresh device observation.</param>
    /// <returns>Safe playback fields and opaque context.</returns>
    private static MediaPlaybackDto Map(MediaPlayback playback) => new(new MediaTargetContextDto(playback.Context.DeviceKey,
        playback.Context.HouseholdId, playback.Context.CoordinatorDeviceKey, playback.Context.MemberDeviceKeys,
        playback.Context.SourceToken, playback.Context.QueueRevision), playback.ObservedAtUtc, playback.Source,
        playback.TransportState, playback.Volume, playback.Muted, playback.GroupVolume, playback.GroupMuted,
        playback.Title, playback.Artist, playback.Album, playback.DurationSeconds, playback.PositionSeconds,
        playback.AvailableActions, playback.PlayMode, playback.Crossfade);

    /// <summary>Projects only operator-visible coordination fields, keeping original uncertain records intact.</summary>
    /// <param name="grant">Authoritative grant status.</param>
    /// <returns>Public control status.</returns>
    private static MediaControlStatusDto Map(MediaGrantStatus grant) => new(grant.Enabled, grant.Generation,
        grant.BlockedOperations.Select(operation =>
        {
            var payload = JsonSerializer.Deserialize<MediaExecutionPayload>(operation.PayloadJson);
            return new MediaBlockDto(operation.OperationId, operation.Targets, payload?.Action ?? "unknown", payload?.Inputs.Value,
                operation.Status);
        }).ToArray());

    /// <summary>Combines a confirmed event receipt with the independent authoritative execution phase.</summary>
    /// <param name="id">Operation identity.</param>
    /// <param name="view">Persisted semantic history.</param>
    /// <param name="admission">Technical execution phase, if admitted.</param>
    /// <returns>The current operation receipt.</returns>
    private static MediaOperationDto Map(Guid id, MediaOperationView view, CoordinatedMediaOperation? admission)
    {
        string status = admission?.Status == "completed" ? "device_acknowledged" : admission?.Status ?? view.State.Status;
        int step = admission?.Step ?? view.State.Step;
        // Revocation can become visible before its semantic audit. A released step already establishes partial-effect risk.
        string? code = status == "revoked" && step >= 0 ? "media.partial.revoked" : admission?.Code ?? view.State.ResultCode;
        return new(id, view.State.Action, view.State.DeviceKey, status, view.State.AcceptedAtUtc, code,
            view.ConcurrencyToken, view.EventSequence, step);
    }

    /// <summary>Validates installation-wide immutable action identity.</summary>
    /// <param name="context">Incoming headers.</param>
    /// <returns>A nonempty canonical UUID.</returns>
    private static Guid Key(HttpContext context) =>
        Guid.TryParseExact(context.Request.Headers["Idempotency-Key"].ToString(), "D", out Guid id) && id != Guid.Empty
            ? id : throw new MediaCoordinationException("request.idempotency_key.invalid", 400);

    /// <summary>Resolves the hardened device port only when the slice is configured.</summary>
    /// <param name="context">Request services.</param>
    /// <returns>Configured device gateway.</returns>
    private static IMediaDeviceGateway Devices(HttpContext context) => Required<IMediaDeviceGateway>(context);

    /// <summary>Resolves the durable coordinator.</summary>
    /// <param name="context">Request services.</param>
    /// <returns>Configured coordination store.</returns>
    private static IMediaCoordinationStore Store(HttpContext context) => Required<IMediaCoordinationStore>(context);

    /// <summary>Reports disabled deployment composition through a stable contract.</summary>
    /// <typeparam name="T">Required slice service.</typeparam>
    /// <param name="context">Request services.</param>
    /// <returns>The configured service.</returns>
    private static T Required<T>(HttpContext context) where T : class => context.RequestServices.GetService<T>()
        ?? throw new MediaCoordinationException("media.backend.disabled", 503);

    /// <summary>Bounds each request and returns safe errors without exposing device metadata or database secrets.</summary>
    /// <param name="context">Incoming request cancellation.</param>
    /// <param name="operation">Endpoint operation.</param>
    /// <returns>A response with stable error codes.</returns>
    private static async Task<IResult> SafeAsync(HttpContext context, Func<HttpContext, Task<IResult>> operation)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        context.RequestAborted = budget.Token;
        try
        {
            return await operation(context);
        }
        catch (MediaCoordinationException exception)
        {
            return Problem(exception.Code, exception.StatusCode);
        }
        catch (MediaDeviceException exception)
        {
            // HTTP device access only reads/prepares. State and capability refusals are definite pre-admission conflicts.
            return Problem("media." + exception.Code, exception.Code is "device_unavailable" or "device_timeout" ? 503 : 409);
        }
        catch (Exception exception) when (exception is NpgsqlException or HttpRequestException or IOException or
            OperationCanceledException or FormatException or JsonException)
        {
            return Problem("media.unavailable", 503);
        }
    }

    /// <summary>Creates consistent language-neutral ProblemDetails.</summary>
    /// <param name="code">Stable safe code.</param>
    /// <param name="status">HTTP status.</param>
    /// <returns>A ProblemDetails response.</returns>
    private static IResult Problem(string code, int status) => Results.Problem(statusCode: status, title: code,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
