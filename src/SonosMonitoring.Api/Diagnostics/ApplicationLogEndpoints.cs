using SonosMonitoring.Observability.Diagnostics;

namespace SonosMonitoring.Api.Diagnostics;

/// <summary>Exposes bounded operational logs behind the host's existing access boundary.</summary>
public static class ApplicationLogEndpoints
{
    /// <summary>Maps read-only log queries without accepting filesystem paths from callers.</summary>
    /// <param name="app">API host with access middleware already configured.</param>
    public static void MapApplicationLogs(this WebApplication app)
    {
        app.MapGet("/api/diagnostics/logs", (ApplicationLogStore store, string? source, string? minimumLevel, string? search, int? limit) =>
        {
            try
            {
                return Results.Ok(store.Read(string.IsNullOrEmpty(source) ? null : source, minimumLevel, search, limit ?? 200));
            }
            catch (ArgumentException)
            {
                return Results.Problem(statusCode: 400, title: "logs.query.invalid");
            }
        });
    }
}
