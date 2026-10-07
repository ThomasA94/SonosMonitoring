using SonosMonitoring.Application.Metrics;
using SonosMonitoring.Contracts.Metrics;

namespace SonosMonitoring.Api.Metrics;

/// <summary>Exposes safe, bounded historical queries and their shared selection catalog.</summary>
public static class HistoryEndpoints
{
    /// <summary>Registers protected historical reads and the matching selector catalog.</summary>
    /// <param name="app">The API host.</param>
    public static void MapMetricHistory(this WebApplication app)
    {
        app.MapGet("/api/metrics/history/catalog", () => Results.Ok(new MetricCatalogResponse(
            MetricHistoryCatalog.Definitions.Select(definition => new MetricCatalogItem(
                definition.Selector, definition.Unit, definition.Scope, definition.LabelKey)).ToArray())));
        app.MapGet("/api/metrics/history", ReadAsync);
    }

    /// <summary>Queries the common API/Grafana data source.</summary>
    /// <param name="metric">Supported selector from the shared catalog.</param>
    /// <param name="deviceKey">The stable device identity.</param>
    /// <param name="networkInterface">Optional interface filter.</param>
    /// <param name="sourceFilter">Optional source filter for source scoped selectors.</param>
    /// <param name="start">Inclusive start time.</param>
    /// <param name="end">Inclusive end time.</param>
    /// <param name="stepSeconds">Requested result resolution.</param>
    /// <param name="context">Request scope and cancellation.</param>
    private static async Task<IResult> ReadAsync(string metric, string deviceKey, string? networkInterface, string? sourceFilter,
        DateTimeOffset start, DateTimeOffset end, int stepSeconds, HttpContext context)
    {
        var history = context.RequestServices.GetService<IMetricHistory>();
        if (history is null)
        {
            return Results.Problem(statusCode: 503, title: "history.unconfigured");
        }

        MetricHistoryDefinition? definition = MetricHistoryCatalog.Find(metric);
        if (definition is null)
        {
            return Results.Problem(statusCode: 400, title: "history.query.invalid");
        }

        try
        {
            var request = new MetricHistoryRequest(metric, deviceKey, networkInterface, start, end, stepSeconds)
            {
                Source = sourceFilter,
            };
            var data = await history.ReadAsync(request, context.RequestAborted);
            return Results.Ok(new MetricHistoryResponse(metric, deviceKey, definition.Unit, stepSeconds, start, end, data));
        }
        catch (ArgumentException)
        {
            return Results.Problem(statusCode: 400, title: "history.query.invalid");
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException ||
            (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested == false))
        {
            return Results.Problem(statusCode: 503, title: "history.unavailable");
        }
    }
}
