using SonosMonitoring.Contracts.Collection;
using SonosMonitoring.Infrastructure.Collection.Configuration;

namespace SonosMonitoring.Api.Collection;

/// <summary>Exposes protected technical acquisition settings separately from business commands.</summary>
public static class CollectorConfigurationEndpoints
{
    /// <summary>Maps reads and revision-checked saves using the API's existing access boundary.</summary>
    /// <param name="app">API route builder.</param>
    public static void MapCollectorConfiguration(this WebApplication app)
    {
        app.MapGet("/api/collection/configuration", (CollectorRuntime runtime, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var document = runtime.GetConfiguration();
            return Results.Ok(new CollectorConfigurationDto(document.Revision, CollectorSettingsMapper.Map(document.Options)));
        });
        app.MapPost("/api/collection/configuration", async (CollectorConfigurationDto request, CollectorRuntime runtime, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var requestId) == false || requestId == Guid.Empty ||
                request.Settings is null || request.Settings.Gena is null)
            {
                return Results.Problem(statusCode: 400, title: "collection.configuration.invalid");
            }
            try
            {
                var options = CollectorSettingsMapper.Map(request.Settings, runtime.GetConfiguration().Options.AllowLoopback);
                bool accepted = await runtime.ApplyAsync(options, request.Revision, requestId);
                if (accepted == false)
                {
                    return Results.Problem(statusCode: 409, title: "request.conflict");
                }
                return Results.Ok(new CollectorConfigurationDto(requestId, CollectorSettingsMapper.Map(options)));
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
            {
                return Results.Problem(statusCode: 400, title: "collection.configuration.invalid");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Results.Problem(statusCode: 503, title: "collection.configuration.storage");
            }
        });
    }
}
