namespace SonosMonitoring.Application.Metrics;

/// <summary>Describes one selectable metric and the labels its history series use.</summary>
/// <param name="Selector">Stable API selector.</param>
/// <param name="Unit">Display unit for returned values.</param>
/// <param name="Scope">Primary label scope: interface, source, or device.</param>
/// <param name="LabelKey">Prometheus label used for the primary scope.</param>
public sealed record MetricHistoryDefinition(string Selector, string Unit, string Scope, string LabelKey);
