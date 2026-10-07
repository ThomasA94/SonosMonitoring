namespace SonosMonitoring.Contracts.Collection;

/// <summary>Portable diagnostic document; snapshot observations are not an atomic household state or a backup.</summary>
/// <param name="Format">Document discriminator: sonosmonitoring.collection-snapshot.</param>
/// <param name="SchemaVersion">Version of the entire document, including the nested snapshot contract.</param>
/// <param name="CapturedAtUtc">UTC time after the collector view was captured.</param>
/// <param name="Privacy">Applied reduction policy: reduced-identifiers-v1. This does not guarantee anonymity.</param>
/// <param name="Snapshot">Detached observations; identity, address, room, group and interface fields contain local aliases.</param>
public sealed record CollectorSnapshotExportDto(string Format, int SchemaVersion, DateTimeOffset CapturedAtUtc,
    string Privacy, CollectorSnapshotDto Snapshot);
