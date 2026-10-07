using SonosMonitoring.Application.Collection;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Api.Collection;

/// <summary>Publishes live observations and metrics from one collector snapshot per request.</summary>
public static class CollectionEndpoints
{
    /// <summary>Registers read-only collection and scrape endpoints.</summary>
    /// <param name="app">The API host.</param>
    public static void MapCollection(this WebApplication app)
    {
        app.MapGet("/api/collection", (ICollectorSnapshotReader reader) => Results.Ok(CollectionSnapshotMapper.Map(reader.GetSnapshot())));
        app.MapGet("/metrics", async (ICollectorSnapshotReader reader, HttpContext context) =>
            Results.Text(CollectorMetricsFormatter.Format(reader.GetSnapshot()) +
                await SonosMonitoring.Infrastructure.MediaControl.MediaMetricsFormatter.FormatAsync(
                    context.RequestServices.GetService<SonosMonitoring.Application.MediaControl.IMediaCoordinationStore>(),
                    context.RequestAborted), "text/plain; version=0.0.4; charset=utf-8"));
        app.MapGet("/api/metrics/catalog", () => Results.Ok(CollectorMetricsFormatter.Catalog));
    }

}
