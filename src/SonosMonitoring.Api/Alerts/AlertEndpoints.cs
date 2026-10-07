using SonosMonitoring.Application.Alerts;
using SonosMonitoring.Contracts.Alerts;

namespace SonosMonitoring.Api.Alerts;

/// <summary>Exposes current internal messages without adding acknowledgement or history semantics.</summary>
public static class AlertEndpoints
{
    /// <summary>Registers the current alert-message query.</summary>
    /// <param name="app">The API host.</param>
    public static void MapAlerts(this WebApplication app) => app.MapGet("/api/alerts", (Func<HttpContext, Task<IResult>>)ReadAsync);

    /// <summary>Reads only validated, recent rule output and maps known fields to the public contract.</summary>
    /// <param name="context">Request scope and cancellation.</param>
    private static async Task<IResult> ReadAsync(HttpContext context)
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        IAlertMessagesReader? reader = context.RequestServices.GetService<IAlertMessagesReader>();
        if (reader is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "alerts.unconfigured");
        }

        try
        {
            AlertMessagesSnapshot snapshot = await reader.ReadAsync(context.RequestAborted);
            AlertMessagesSnapshotDto result = new(snapshot.EvaluatedAtUtc, snapshot.EvaluationDurationSeconds,
                snapshot.Messages.Select(message => new AlertMessageDto(message.Identity, message.OccurrenceIdentity,
                    message.RuleName, message.State, message.ActiveAtUtc, message.RecoveryAtUtc,
                    message.EvaluatedAtUtc, message.EvaluationDurationSeconds, message.Severity, message.Scope,
                    message.DeviceLabel)).ToArray());
            return Results.Ok(result);
        }
        catch (HttpRequestException)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "alerts.upstream_unavailable");
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "alerts.upstream_unavailable");
        }
        catch (IOException)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "alerts.upstream_unavailable");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested == false)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "alerts.upstream_unavailable");
        }
    }
}
