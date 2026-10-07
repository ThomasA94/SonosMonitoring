namespace SonosMonitoring.Application.Collection.Diagnostics;

/// <summary>Contains transport state values reported by AVTransport.</summary>
/// <param name="TransportState">The reported transport state.</param>
/// <param name="TransportStatus">The reported transport status, or <see langword="null"/> when unavailable.</param>
public sealed record PlaybackObservation(string TransportState, string? TransportStatus);
