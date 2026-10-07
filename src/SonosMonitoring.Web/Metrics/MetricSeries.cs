namespace SonosMonitoring.Web.Metrics;

/// <summary>Represents a single returned label set without merging distinct interfaces or sources.</summary>
/// <param name="Label">Readable interface or source labels.</param>
/// <param name="Points">Time-ordered measurements; null values are explicit gaps.</param>
public sealed record MetricSeries(string Label, IReadOnlyList<MetricPoint> Points);

/// <summary>Represents one Prometheus evaluation, preserving nonfinite samples as gaps.</summary>
/// <param name="Time">UTC evaluation time.</param>
/// <param name="Value">Finite value, or null for missing/nonfinite data.</param>
public sealed record MetricPoint(DateTimeOffset Time, double? Value);
