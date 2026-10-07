namespace SonosMonitoring.Projections;

/// <summary>Runs the independent standard raw/merged projection worker.</summary>
public static class Program
{
    /// <summary>Starts the projection worker with external infrastructure configuration.</summary>
    /// <param name="args">Host configuration arguments.</param>
    public static async Task Main(string[] args)
    {
        await using var host = ProjectionHost.Build(args);
        await host.RunAsync();
    }
}
