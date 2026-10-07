using Microsoft.Extensions.Logging;
using SonosMonitoring.Application.Collection;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Checks actionable network evidence without per-address scan noise or invented root causes.</summary>
public sealed class CollectorNetworkDiagnosticsTests
{
    /// <summary>Records one loss and one recovery while repeated failure and newly discovered players stay quiet.</summary>
    [Fact]
    public void LogsOnlyKnownPlayerReachabilityTransitions()
    {
        CollectorState state = new(new CollectorOptions());
        PlayerDescription player = new("RINCON_FIRST", "Office", "Test");
        state.DescriptionSucceeded(player, "192.168.6.2", DateTimeOffset.UtcNow);
        CollectorSnapshot healthy = state.GetSnapshot();
        NetworkDiagnosticTestLogger logger = new();
        CollectorNetworkDiagnostics.LogTransitions(logger, [], healthy.Devices);
        Assert.Empty(logger.Entries);

        state.DescriptionFailed("192.168.6.2", DateTimeOffset.UtcNow, "timeout");
        CollectorSnapshot failed = state.GetSnapshot();
        CollectorNetworkDiagnostics.LogTransitions(logger, healthy.Devices, failed.Devices);
        CollectorNetworkDiagnostics.LogTransitions(logger, failed.Devices, state.GetSnapshot().Devices);
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("192.168.6.2", warning.Message);
        Assert.Contains("timeout", warning.Message);
        Assert.Contains("possible cause, not a confirmed diagnosis", warning.Message);

        state.DescriptionSucceeded(player, "192.168.6.3", DateTimeOffset.UtcNow);
        CollectorNetworkDiagnostics.LogTransitions(logger, failed.Devices, state.GetSnapshot().Devices);
        Assert.Equal(2, logger.Entries.Count);
        Assert.Equal(LogLevel.Information, logger.Entries[1].Level);
        Assert.Contains("192.168.6.3 is reachable again", logger.Entries[1].Message);
    }

    /// <summary>Summarizes empty discovery with routes as a hypothesis and keeps all attempt counts visible.</summary>
    [Fact]
    public void EmptyInterruptedDiscoveryWarnsWithCountsAndCidr()
    {
        NetworkDiagnosticTestLogger logger = new();
        CollectorNetworkDiagnostics.LogDiscovery(logger, ["192.168.6.0/24", "10.1.0.0/24"], 508, 40, 0, 38, true);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("2 ranges [192.168.6.0/24, 10.1.0.0/24]", entry.Message);
        Assert.Contains("508 scheduled, 40 probed, 0 found, 40 failed (including 38 timeouts), 468 unprobed", entry.Message);
        Assert.Contains("Docker/LAN subnet overlap is a possible cause", entry.Message);
    }

    /// <summary>Normal unused-address timeouts do not create warnings when discovery successfully completes.</summary>
    [Fact]
    public void SuccessfulDiscoveryEmitsOneInformationalSummary()
    {
        NetworkDiagnosticTestLogger logger = new();
        CollectorNetworkDiagnostics.LogDiscovery(logger, ["192.168.6.0/24"], 254, 254, 4, 250, false);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("4 found, 250 failed (including 250 timeouts), 0 unprobed", entry.Message);
        Assert.DoesNotContain("Docker/LAN", entry.Message);
    }
}
