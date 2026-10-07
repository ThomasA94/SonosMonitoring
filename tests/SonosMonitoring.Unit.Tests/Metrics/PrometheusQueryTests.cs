using SonosMonitoring.Application.Metrics;
using SonosMonitoring.Infrastructure.Metrics;

namespace SonosMonitoring.Unit.Tests.Metrics;

/// <summary>Protects bounded history selection, units, labels, and generated PromQL.</summary>
public sealed class PrometheusQueryTests
{
    /// <summary>Every catalog selector has a unit and builds only its expected bounded metric.</summary>
    [Fact]
    public void EveryCatalogSelectorBuildsWithItsCatalogUnit()
    {
        Dictionary<string, string> expectedUnits = new(StringComparer.Ordinal)
        {
            ["receive_bytes"] = "bytes",
            ["transmit_bytes"] = "bytes",
            ["receive_rate"] = "bytes/second",
            ["transmit_rate"] = "bytes/second",
            ["probe_success"] = "boolean",
            ["receive_packets"] = "packets",
            ["transmit_packets"] = "packets",
            ["receive_errors"] = "errors",
            ["transmit_errors"] = "errors",
            ["receive_dropped"] = "drops",
            ["transmit_dropped"] = "drops",
            ["receive_packets_rate"] = "packets/second",
            ["transmit_packets_rate"] = "packets/second",
            ["receive_errors_rate"] = "errors/second",
            ["transmit_errors_rate"] = "errors/second",
            ["receive_dropped_rate"] = "drops/second",
            ["transmit_dropped_rate"] = "drops/second",
            ["source_success"] = "boolean",
            ["source_fresh"] = "boolean",
            ["source_age"] = "seconds",
            ["gena_subscribed"] = "boolean",
            ["gena_initial_received"] = "boolean",
            ["gena_last_notification_age"] = "seconds",
            ["probe_duration"] = "seconds",
            ["volume"] = "percent",
            ["muted"] = "boolean",
        };
        Assert.Equal(expectedUnits.Count, MetricHistoryCatalog.Definitions.Count);

        foreach (MetricHistoryDefinition definition in MetricHistoryCatalog.Definitions)
        {
            var request = this.CreateRequest(definition.Selector);
            string expression = PrometheusQuery.Build(request);
            Assert.Equal(expectedUnits[definition.Selector], definition.Unit);
            Assert.Contains("device_id=\"", expression);
            Assert.DoesNotContain("RINCON_TEST", expression);
        }
    }

    /// <summary>Counter rates use reset-aware PromQL and the same two-minute window as Grafana.</summary>
    [Fact]
    public void RateUsesTheSameCounterAndWindowAsGrafana()
    {
        string expression = PrometheusQuery.Build(this.CreateRequest("receive_rate", networkInterface: "br0"));
        Assert.StartsWith("rate(sonos_interface_receive_bytes_total{device_id=\"", expression);
        Assert.EndsWith(",interface=\"br0\"}[2m])", expression);
        Assert.DoesNotContain("RINCON_TEST", expression);
    }

    /// <summary>Source age is derived from the last successful observation and never becomes negative.</summary>
    [Fact]
    public void SourceAgeUsesClampedAgeExpression()
    {
        string expression = PrometheusQuery.Build(this.CreateRequest("source_age", source: "topology"));
        Assert.StartsWith("clamp_min(time() - sonos_source_last_success_timestamp_seconds{device_id=\"", expression);
        Assert.EndsWith(",source=\"topology\"}, 0)", expression);
    }

    /// <summary>GENA notification age uses the last accepted notification timestamp and remains nonnegative.</summary>
    [Fact]
    public void GenaNotificationAgeUsesClampedTimestampExpression()
    {
        string expression = PrometheusQuery.Build(this.CreateRequest("gena_last_notification_age"));
        Assert.StartsWith("clamp_min(time() - sonos_device_gena_last_notification_timestamp_seconds{device_id=\"", expression);
        Assert.EndsWith("}, 0)", expression);
    }

    /// <summary>Each allowlisted source can be selected and appears only as a quoted source label.</summary>
    [Theory]
    [InlineData("description")]
    [InlineData("ifconfig")]
    [InlineData("topology")]
    [InlineData("wireless")]
    [InlineData("ethernet")]
    [InlineData("playback")]
    [InlineData("volume")]
    public void AcceptsAllowlistedSourceFilters(string source)
    {
        string expression = PrometheusQuery.Build(this.CreateRequest("source_success", source: source));
        Assert.Contains($",source=\"{source}\"", expression);
    }

    /// <summary>Unsupported expressions, injected labels, and invalid bounds never reach Prometheus.</summary>
    [Theory]
    [InlineData("up", "RINCON_TEST", "br0", null, 15)]
    [InlineData("receive_bytes", "bad", "br0", null, 15)]
    [InlineData("receive_bytes", "RINCON_TEST", "br0\"} or up", null, 15)]
    [InlineData("receive_bytes", "RINCON_TEST", "br0", null, 0)]
    [InlineData("receive_bytes", "RINCON_TEST", null, "topology", 15)]
    [InlineData("source_success", "RINCON_TEST", null, "topology\"} or up", 15)]
    [InlineData("source_success", "RINCON_TEST", null, "unknown", 15)]
    public void RejectsInvalidInputs(string metric, string device, string? networkInterface, string? source, int step)
    {
        var request = this.CreateRequest(metric, device, networkInterface, source) with { StepSeconds = step };
        Assert.Throws<ArgumentException>(() => PrometheusQuery.Build(request));
    }

    /// <summary>Long time ranges require an explicit coarse response resolution.</summary>
    [Fact]
    public void EnforcesPointBudget()
    {
        var request = this.CreateRequest("receive_bytes") with { End = DateTimeOffset.UnixEpoch.AddDays(400) };
        Assert.Throws<ArgumentException>(() => PrometheusQuery.Build(request));
        Assert.NotEmpty(PrometheusQuery.Build(request with { StepSeconds = 3600 }));
    }

    /// <summary>Non-increasing ranges and ranges beyond the documented maximum are rejected.</summary>
    [Fact]
    public void RejectsInvalidTimeBounds()
    {
        var request = this.CreateRequest("receive_bytes");
        Assert.Throws<ArgumentException>(() => PrometheusQuery.Build(request with { End = request.Start }));
        Assert.Throws<ArgumentException>(() => PrometheusQuery.Build(request with { End = request.Start.AddDays(4001), StepSeconds = 4000 }));
    }

    /// <summary>Creates one representative bounded history request for selector tests.</summary>
    /// <param name="metric">Catalog selector.</param>
    /// <param name="device">Device identifier.</param>
    /// <param name="networkInterface">Optional interface filter.</param>
    /// <param name="source">Optional source filter.</param>
    private MetricHistoryRequest CreateRequest(string metric, string device = "RINCON_TEST", string? networkInterface = null, string? source = null) =>
        new(metric, device, networkInterface, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(5), 15)
        {
            Source = source,
        };
}
