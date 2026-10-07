using System.Text.Json;
using SonosMonitoring.Application.Collection;
using SonosMonitoring.Contracts.Collection;

namespace SonosMonitoring.Api.Collection;

/// <summary>Downloads a privacy-reduced diagnostic document using the host's existing access boundary.</summary>
public static class CollectionExportEndpoints
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Registers read-only snapshot capture; collection is never triggered by a download.</summary>
    /// <param name="app">API host with local access middleware.</param>
    public static void MapCollectionExport(this WebApplication app)
    {
        app.MapGet("/api/collection/export", (ICollectorSnapshotReader reader, HttpContext context) =>
        {
            var snapshot = reader.GetSnapshot();
            var document = new CollectorSnapshotExportDto("sonosmonitoring.collection-snapshot", 1, DateTimeOffset.UtcNow,
                "reduced-identifiers-v1", CollectionSnapshotMapper.Map(CollectorSnapshotRedactor.Redact(snapshot), includeMetricLabels: false));
            context.Response.Headers.CacheControl = "no-store";
            return Results.File(JsonSerializer.SerializeToUtf8Bytes(document, _jsonOptions), "application/json; charset=utf-8",
                "sonosmonitoring-snapshot.json");
        });
    }
}
