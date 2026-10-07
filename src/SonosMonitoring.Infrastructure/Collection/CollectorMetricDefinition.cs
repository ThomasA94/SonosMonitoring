namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Public name, Prometheus type, and meaning of one collector metric.</summary>
/// <param name="Name">Prometheus metric name.</param>
/// <param name="Type">Prometheus metric type.</param>
/// <param name="Help">Human-readable metric meaning.</param>
public sealed record CollectorMetricDefinition(string Name, string Type, string Help);
