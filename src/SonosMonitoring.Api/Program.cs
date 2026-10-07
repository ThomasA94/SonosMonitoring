namespace SonosMonitoring.Api;

/// <summary>Starts the backend or executes a catalogue operation before any services connect.</summary>
public static class Program
{
    /// <summary>Runs the standalone API process.</summary>
    /// <param name="args">Host or framework catalogue arguments.</param>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--write-media-control-schema")
        {
            await File.WriteAllTextAsync(args[1], Infrastructure.MediaControl.MediaCoordinationSchema.GetPostgresScript());
            return 0;
        }
        if (args.Length == 2 && args[0] == "--write-observation-history-schema")
        {
            await File.WriteAllTextAsync(args[1], Infrastructure.History.ObservationHistorySchema.GetPostgresScript());
            return 0;
        }
        if (args.Length == 2 && args[0] == "--write-event-store-schema")
        {
            await File.WriteAllTextAsync(args[1], Devbone.Frameworks.EventSourcing.Sql.EventStoreSchema.GetPostgresScript());
            return 0;
        }
        if (args.Length == 2 && args[0] == "--write-metric-catalog")
        {
            await File.WriteAllTextAsync(args[1], System.Text.Json.JsonSerializer.Serialize(Infrastructure.Collection.CollectorMetricsFormatter.Catalog,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            return 0;
        }
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(Domain.Monitoring.DeviceMonitoring).Module.ModuleHandle);
        if (Devbone.Frameworks.Ddd.Catalog.EventContractCatalogCommand.TryHandle(args,
            () => Devbone.Frameworks.Ddd.Catalog.EventContractCatalog.CreateDocument("SonosMonitoring",
                Devbone.Frameworks.EventSourcing.Ddd.Catalog.AggregateRoutingCatalogExtensions.ToCatalogRoutes(Infrastructure.Monitoring.MonitoringRouting.Create())),
            out var exitCode))
        {
            return exitCode;
        }
        await using var app = ApiHost.Build(args);
        await app.RunAsync();
        return 0;
    }
}
