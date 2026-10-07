namespace SonosMonitoring.Contracts.Metrics;

/// <summary>Describes one selectable metric in the history API.</summary>
/// <param name="Selector">Value accepted by the history endpoint.</param>
/// <param name="Unit">Unit of the returned values.</param>
/// <param name="Scope">Primary series scope: interface, source, or device.</param>
/// <param name="LabelKey">Prometheus label identifying that scope.</param>
public sealed record MetricCatalogItem(string Selector, string Unit, string Scope, string LabelKey);
