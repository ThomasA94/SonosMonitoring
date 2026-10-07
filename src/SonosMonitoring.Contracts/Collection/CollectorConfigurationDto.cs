namespace SonosMonitoring.Contracts.Collection;

/// <summary>A persisted technical configuration with an opaque concurrency revision.</summary>
/// <param name="Revision">Revision required when saving changes.</param>
/// <param name="Settings">All user-configurable acquisition options.</param>
public sealed record CollectorConfigurationDto(Guid Revision, CollectorSettingsDto Settings);
