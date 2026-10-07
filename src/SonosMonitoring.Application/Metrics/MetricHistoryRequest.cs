namespace SonosMonitoring.Application.Metrics;

/// <summary>Describes one bounded history query without exposing a free-form query language.</summary>
/// <param name="Metric">Allowlisted metric selector.</param>
/// <param name="DeviceKey">Canonical Sonos device key.</param>
/// <param name="Interface">Optional interface label.</param>
/// <param name="Start">Inclusive UTC start.</param>
/// <param name="End">Inclusive UTC end.</param>
/// <param name="StepSeconds">Response resolution in seconds.</param>
public sealed record MetricHistoryRequest(string Metric, string DeviceKey, string? Interface, DateTimeOffset Start, DateTimeOffset End, int StepSeconds)
{
    /// <summary>Optional allowlisted source label, used only by source scoped selectors.</summary>
    public string? Source { get; init; }
}
