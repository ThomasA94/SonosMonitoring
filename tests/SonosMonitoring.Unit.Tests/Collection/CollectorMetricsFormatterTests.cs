using SonosMonitoring.Application.Collection;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Verifies safe metric labels and freshness-aware raw counter export.</summary>
public sealed class CollectorMetricsFormatterTests
{
    /// <summary>Distinguishes disabled, never-started and running collectors without inventing a heartbeat.</summary>
    /// <param name="enabled">Whether collection is configured.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExposesCollectionIntentAndConfiguredHeartbeatBudget(bool enabled)
    {
        CollectorState state = new(new CollectorOptions
        {
            Enabled = enabled,
            PollInterval = TimeSpan.FromSeconds(60),
            SweepTimeout = TimeSpan.FromSeconds(120)
        });
        string initial = CollectorMetricsFormatter.Format(state.GetSnapshot());
        Assert.Contains($"sonos_collector_enabled {(enabled ? 1 : 0)}\n", initial);
        Assert.Contains("sonos_collector_heartbeat_max_age_seconds 185\n", initial);
        Assert.DoesNotContain("\nsonos_collector_heartbeat_timestamp_seconds ", initial);

        DateTimeOffset heartbeat = DateTimeOffset.UnixEpoch.AddSeconds(123);
        state.Heartbeat(heartbeat);
        string running = CollectorMetricsFormatter.Format(state.GetSnapshot());
        Assert.Contains("\nsonos_collector_heartbeat_timestamp_seconds 123\n", running);
    }

    /// <summary>Omits expired counters while retaining source health and last success time.</summary>
    [Fact]
    public void OmitsStaleCounters()
    {
        CollectorState state = new(new CollectorOptions { Enabled = true, StaleAfter = TimeSpan.FromSeconds(1) });
        DateTimeOffset old = DateTimeOffset.UtcNow.AddMinutes(-2);
        state.DescriptionSucceeded(new PlayerDescription("RINCON_ABC", null, null), "192.168.6.1", old);
        state.InterfaceSucceeded("RINCON_ABC", [new InterfaceCounters("br0", 42, 9, 3, 4, 0, 0, 0, 0)], old);

        string scrape = CollectorMetricsFormatter.Format(state.GetSnapshot());

        Assert.DoesNotContain("sonos_interface_receive_bytes_total{", scrape);
        Assert.Contains("sonos_source_last_success_timestamp_seconds{", scrape);
        Assert.DoesNotContain("RINCON_ABC", scrape);
    }

    /// <summary>Distinguishes fresh successful source evidence from old successes and new failed attempts.</summary>
    [Fact]
    public void RecoveryEvidenceRequiresFreshSuccessfulSourceObservation()
    {
        CollectorState state = new(new CollectorOptions { Enabled = true, StaleAfter = TimeSpan.FromSeconds(45) });
        string label = CollectorMetricsFormatter.DeviceLabel("RINCON_ABC");
        string metric = $"sonos_source_observation_fresh{{device_id=\"{label}\",source=\"description\"}}";
        state.DescriptionSucceeded(new PlayerDescription("RINCON_ABC", null, null), "192.0.2.1", DateTimeOffset.UtcNow.AddMinutes(-2));
        Assert.Contains($"{metric} 0\n", CollectorMetricsFormatter.Format(state.GetSnapshot()));
        state.DescriptionSucceeded(new PlayerDescription("RINCON_ABC", null, null), "192.0.2.1", DateTimeOffset.UtcNow);
        Assert.Contains($"{metric} 1\n", CollectorMetricsFormatter.Format(state.GetSnapshot()));
        state.DescriptionFailed("192.0.2.1", DateTimeOffset.UtcNow, "timeout");
        Assert.Contains($"{metric} 0\n", CollectorMetricsFormatter.Format(state.GetSnapshot()));
    }

    /// <summary>Emits raw decreased device counters without synthesizing a negative rate.</summary>
    [Fact]
    public void EmitsLatestRawCountersAfterReset()
    {
        CollectorState state = new(new CollectorOptions { Enabled = true });
        DateTimeOffset now = DateTimeOffset.UtcNow;
        state.DescriptionSucceeded(new PlayerDescription("RINCON_ABC", null, null), "192.168.6.1", now);
        state.InterfaceSucceeded("RINCON_ABC", [new InterfaceCounters("br0", 100, 0, 0, 0, 0, 0, 0, 0)], now);
        state.InterfaceSucceeded("RINCON_ABC", [new InterfaceCounters("br0", 5, 0, 0, 0, 0, 0, 0, 0)], now);

        string scrape = CollectorMetricsFormatter.Format(state.GetSnapshot());

        Assert.Contains("sonos_interface_receive_bytes_total{", scrape);
        Assert.Contains("interface=\"br0\"} 5\n", scrape);
        Assert.DoesNotContain("} -95", scrape);
    }

    /// <summary>Escapes label delimiters even if a snapshot was constructed outside the parser.</summary>
    [Fact]
    public void EscapesInterfaceLabels()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SourceSnapshot source = new("ifconfig", true, now, now, false, null);
        DeviceSnapshot device = new("RINCON_ABC", "192.168.6.1", null, null, now, now,
            source, source, [new InterfaceCounters("a\"\\\nb", 1, 0, 0, 0, 0, 0, 0, 0)]);
        CollectorSnapshot snapshot = new(true, now, now, [device], [], false);

        string scrape = CollectorMetricsFormatter.Format(snapshot);

        Assert.Contains("interface=\"a\\\"\\\\\\nb\"", scrape);
    }

    /// <summary>Stops exposing counters immediately when policy disallows further collection.</summary>
    [Fact]
    public void OmitsCountersAfterPolicyDenial()
    {
        CollectorState state = new(new CollectorOptions { Enabled = true });
        DateTimeOffset now = DateTimeOffset.UtcNow;
        state.DescriptionSucceeded(new PlayerDescription("RINCON_ABC", null, null), "192.168.6.1", now);
        state.InterfaceSucceeded("RINCON_ABC", [new InterfaceCounters("br0", 42, 0, 0, 0, 0, 0, 0, 0)], now);
        state.InterfaceFailed("RINCON_ABC", now, "monitoring_disabled");

        string scrape = CollectorMetricsFormatter.Format(state.GetSnapshot());

        Assert.DoesNotContain("sonos_interface_receive_bytes_total{", scrape);
        Assert.Contains("sonos_source_scrape_success{", scrape);
    }
}
