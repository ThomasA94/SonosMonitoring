namespace SonosMonitoring.Contracts.Collection;

/// <summary>Read-only local transport state, independent of device reachability.</summary>
/// <param name="TransportState">Reported transport state.</param>
/// <param name="TransportStatus">Reported transport status.</param>
public sealed record PlaybackObservationDto(string TransportState, string? TransportStatus);
