using System.Globalization;
using SonosMonitoring.Application.History;
using SonosMonitoring.Contracts.History;

namespace SonosMonitoring.Api.History;

/// <summary>Exposes bounded observational history through the same access boundary as current collection.</summary>
public static class ObservationHistoryEndpoints
{
    /// <summary>Registers the journal query.</summary>
    /// <param name="app">Protected API host.</param>
    public static void MapObservationHistory(this WebApplication app) =>
        app.MapGet("/api/collection/history", (Func<HttpContext, Task<IResult>>)ReadAsync);

    /// <summary>Validates the cursor and page bound before contacting storage.</summary>
    /// <param name="context">Authenticated request scope.</param>
    private static async Task<IResult> ReadAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var query = context.Request.Query;
        if (long.TryParse(query["after"].FirstOrDefault() ?? "0", NumberStyles.None, CultureInfo.InvariantCulture, out long after) == false ||
            int.TryParse(query["limit"].FirstOrDefault() ?? "100", NumberStyles.None, CultureInfo.InvariantCulture, out int limit) == false ||
            after < 0 || limit is < 1 or > 200 || query.Any(pair => pair.Value.Count != 1) ||
            query.Keys.Any(key => key is not ("after" or "limit" or "deviceId")))
        {
            return Results.Problem(statusCode: 400, title: "history.invalid_query");
        }
        string? deviceId = query["deviceId"].FirstOrDefault();
        if (deviceId is not null && (deviceId.Length is < 1 or > 128 || deviceId.Any(char.IsControl)))
        {
            return Results.Problem(statusCode: 400, title: "history.invalid_query");
        }
        var history = context.RequestServices.GetService<IObservationHistory>();
        if (history is null)
        {
            return Results.Problem(statusCode: 503, title: "history.unconfigured");
        }
        try
        {
            var page = await history.ReadAsync(after, limit, deviceId, context.RequestAborted);
            return Results.Ok(new ObservationPageDto(page.Changes.Select(change => new ObservationChangeDto(change.Sequence,
                change.Kind, change.RecordedAtUtc, change.ObservedAtUtc, Map(change.Before), Map(change.After), change.Reason)).ToArray(),
                page.NextCursor, page.EarliestCursor, page.LastRecordedAtUtc, page.RetentionDays, page.MaxEntries));
        }
        catch (ArgumentException)
        {
            return Results.Problem(statusCode: 400, title: "history.invalid_query");
        }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or IOException or TimeoutException or InvalidOperationException or System.Text.Json.JsonException ||
            (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested == false))
        {
            return Results.Problem(statusCode: 503, title: "history.unavailable");
        }
    }

    /// <summary>Maps known historical fields without creating a contract-to-application dependency.</summary>
    /// <param name="state">Optional historical observation.</param>
    private static ObservedDeviceDto? Map(ObservedDevice? state) => state is null ? null : new(state.DeviceId, state.Address,
        state.DescriptionState, state.TopologyState, state.Groups.Select(group => new ObservedGroupDto(group.GroupId,
            group.CoordinatorDeviceId, group.Members.Select(member => new ObservedMemberDto(member.DeviceId, member.ParentDeviceId)).ToArray())).ToArray());
}
