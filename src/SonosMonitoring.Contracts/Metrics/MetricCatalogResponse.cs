namespace SonosMonitoring.Contracts.Metrics;

/// <summary>Returns the safe selection catalog for historical metric queries.</summary>
/// <param name="Metrics">Supported selectors and their display metadata.</param>
public sealed record MetricCatalogResponse(IReadOnlyList<MetricCatalogItem> Metrics);
