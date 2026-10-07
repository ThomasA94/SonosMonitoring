using System.Text.Json;

namespace SonosMonitoring.Application.Metrics;

/// <summary>Queries the same historical data source consumed independently by Grafana.</summary>
public interface IMetricHistory
{
    /// <summary>Queries bounded history; missing observations remain absent from the result.</summary>
    /// <param name="request">Validated query bounds and selectors.</param>
    /// <param name="ct">Request cancellation.</param>
    Task<JsonElement> ReadAsync(MetricHistoryRequest request, CancellationToken ct);
}
