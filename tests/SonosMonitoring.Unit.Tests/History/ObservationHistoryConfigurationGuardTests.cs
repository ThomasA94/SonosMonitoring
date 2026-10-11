using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SonosMonitoring.Infrastructure.Collection;
using SonosMonitoring.Infrastructure.Collection.Configuration;
using SonosMonitoring.Infrastructure.History;

namespace SonosMonitoring.Unit.Tests.History;

/// <summary>Checks that effective collector settings respect the enabled history writer capacity.</summary>
public sealed class ObservationHistoryConfigurationGuardTests
{
    /// <summary>Accepts the writer boundary and rejects invalid changes before durable save.</summary>
    [Fact]
    public async Task RuntimeRejectsOversizedSaveWithoutReplacingConfiguration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sonos-history-guard-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "collector.json");
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(directory));
        using ServiceProvider host = services.BuildServiceProvider();
        CollectorConfigurationStore store = new(path);
        ObservationHistoryConfigurationGuard guard = new();
        CollectorOptions defaults = new();
        await using CollectorRuntime runtime = new(host, store, defaults, guard);

        CollectorOptions oversized = new() { MaxKnownDevices = ObservationHistoryLimits.MAX_KNOWN_DEVICES + 1 };
        await Assert.ThrowsAsync<ArgumentException>(() => runtime.ApplyAsync(oversized, Guid.Empty, Guid.NewGuid()));

        Assert.False(File.Exists(path));
        guard.Validate(new CollectorOptions { MaxKnownDevices = ObservationHistoryLimits.MAX_KNOWN_DEVICES });
        if (Directory.Exists(directory))
        { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>Rejects saved values above the writer limit during runtime construction.</summary>
    [Fact]
    public void RuntimeRejectsOversizedPreviouslySavedConfiguration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sonos-history-saved-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "collector.json");
        CollectorConfigurationStore store = new(path);
        store.Save(new CollectorConfigurationDocument(Guid.NewGuid(),
            new CollectorOptions { MaxKnownDevices = ObservationHistoryLimits.MAX_KNOWN_DEVICES + 1 }));
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(directory));
        using ServiceProvider host = services.BuildServiceProvider();

        Assert.Throws<ArgumentException>(() => new CollectorRuntime(host, store, new CollectorOptions(),
            new ObservationHistoryConfigurationGuard()));

        if (Directory.Exists(directory))
        { Directory.Delete(directory, recursive: true); }
    }
}
