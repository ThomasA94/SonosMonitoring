namespace SonosMonitoring.Infrastructure.Collection.Configuration;

/// <summary>Durable technical configuration, distinct from event-sourced device monitoring policies.</summary>
/// <param name="Revision">Last accepted save identity and concurrency revision.</param>
/// <param name="Options">Validated acquisition options.</param>
public sealed record CollectorConfigurationDocument(Guid Revision, CollectorOptions Options);
