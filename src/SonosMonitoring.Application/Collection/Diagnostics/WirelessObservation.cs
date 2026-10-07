namespace SonosMonitoring.Application.Collection.Diagnostics;

/// <summary>Contains supported values reported by the wireless status endpoint.</summary>
/// <param name="WifiMode">Reported Wi-Fi mode, or <see langword="null"/> when unavailable.</param>
/// <param name="ConnectionType">Reported connection type, or <see langword="null"/> when unavailable.</param>
/// <param name="SonosNetDisabled">Whether SonosNet is disabled, or <see langword="null"/> when unavailable.</param>
public sealed record WirelessObservation(string? WifiMode, string? ConnectionType, bool? SonosNetDisabled);
