using System.Globalization;
using System.Text.RegularExpressions;
using SonosMonitoring.Application.Metrics;
using SonosMonitoring.Domain.Monitoring;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Infrastructure.Metrics;

/// <summary>Builds Prometheus expressions from the shared, bounded history selector catalog.</summary>
public static class PrometheusQuery
{
    /// <summary>Builds an allowlisted expression and rejects unbounded ranges or label injection.</summary>
    /// <param name="request">A single device history query.</param>
    /// <returns>A PromQL expression generated only from catalog entries.</returns>
    public static string Build(MetricHistoryRequest request)
    {
        if (DeviceMonitoring.ValidatePolicy(request.DeviceKey, 15).Success == false ||
            request.End <= request.Start || request.StepSeconds < 15 ||
            (request.End - request.Start).TotalSeconds / request.StepSeconds > 11000 ||
            request.End - request.Start > TimeSpan.FromDays(4000))
        {
            throw new ArgumentException("History bounds or device identity are invalid.");
        }

        MetricHistoryDefinition? definition = MetricHistoryCatalog.Find(request.Metric);
        if (definition is null)
        {
            throw new ArgumentException("Metric is not supported.");
        }

        if (request.Interface is not null &&
            (definition.Scope != "interface" || Regex.IsMatch(request.Interface, "^[A-Za-z][A-Za-z0-9_.-]{0,15}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) == false))
        {
            throw new ArgumentException("Interface label is invalid for this metric.");
        }

        if (request.Source is not null &&
            (definition.Scope != "source" || MetricHistoryCatalog.AllowedSources.Contains(request.Source) == false))
        {
            throw new ArgumentException("Source label is invalid for this metric.");
        }

        string metricName = request.Metric switch
        {
            "receive_bytes" or "receive_rate" => "sonos_interface_receive_bytes_total",
            "transmit_bytes" or "transmit_rate" => "sonos_interface_transmit_bytes_total",
            "receive_packets" or "receive_packets_rate" => "sonos_interface_receive_packets_total",
            "transmit_packets" or "transmit_packets_rate" => "sonos_interface_transmit_packets_total",
            "receive_errors" or "receive_errors_rate" => "sonos_interface_receive_errors_total",
            "transmit_errors" or "transmit_errors_rate" => "sonos_interface_transmit_errors_total",
            "receive_dropped" or "receive_dropped_rate" => "sonos_interface_receive_dropped_total",
            "transmit_dropped" or "transmit_dropped_rate" => "sonos_interface_transmit_dropped_total",
            "probe_success" => "sonos_device_probe_success",
            "source_success" => "sonos_source_scrape_success",
            "source_fresh" => "sonos_source_observation_fresh",
            "source_age" => "sonos_source_last_success_timestamp_seconds",
            "gena_subscribed" => "sonos_device_gena_subscribed",
            "gena_initial_received" => "sonos_device_gena_initial_received",
            "gena_last_notification_age" => "sonos_device_gena_last_notification_timestamp_seconds",
            "probe_duration" => "sonos_device_probe_duration_seconds",
            "volume" => "sonos_device_volume",
            "muted" => "sonos_device_muted",
            _ => throw new ArgumentException("Metric is not supported."),
        };

        string labels = $"device_id=\"{CollectorMetricsFormatter.DeviceLabel(request.DeviceKey)}\"";
        if (definition.Scope == "interface" && request.Interface is not null)
        {
            labels += $",interface=\"{request.Interface}\"";
        }
        else if (definition.Scope == "source" && request.Source is not null)
        {
            labels += $",source=\"{request.Source}\"";
        }

        string expression = $"{metricName}{{{labels}}}";
        if (request.Metric is "source_age" or "gena_last_notification_age")
        {
            return $"clamp_min(time() - {expression}, 0)";
        }

        return request.Metric.EndsWith("_rate", StringComparison.Ordinal) ? $"rate({expression}[2m])" : expression;
    }

    /// <summary>Encodes the documented query_range parameters with invariant timestamps.</summary>
    /// <param name="request">The validated request.</param>
    /// <returns>A relative Prometheus HTTP API URL.</returns>
    public static string RelativeUrl(MetricHistoryRequest request) =>
        "api/v1/query_range?query=" + Uri.EscapeDataString(Build(request)) +
        "&start=" + request.Start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) +
        "&end=" + request.End.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) +
        "&step=" + request.StepSeconds.ToString(CultureInfo.InvariantCulture) + "&timeout=5s&limit=16";
}
