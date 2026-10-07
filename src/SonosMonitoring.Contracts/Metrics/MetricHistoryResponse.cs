using System.Text.Json;

namespace SonosMonitoring.Contracts.Metrics;

/// <summary>Returns bounded Prometheus history with the query bounds and authoritative unit.</summary>
/// <param name="Metric">Allowlisted API metric selector.</param>
/// <param name="DeviceKey">Queried device identity.</param>
/// <param name="Unit">Measurement unit selected by the API.</param>
/// <param name="StepSeconds">Requested evaluation resolution.</param>
/// <param name="Start">Inclusive query start.</param>
/// <param name="End">Inclusive query end.</param>
/// <param name="Data">Prometheus matrix data, including labels and timestamp/value pairs.</param>
public sealed record MetricHistoryResponse(string Metric, string DeviceKey, string Unit, int StepSeconds,
    DateTimeOffset Start, DateTimeOffset End, JsonElement Data);
