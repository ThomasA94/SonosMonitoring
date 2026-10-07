namespace SonosMonitoring.Contracts.Collection;

/// <summary>Reported wireless mode, without interpreting diagnostic values as signal quality.</summary>
/// <param name="WifiMode">Reported Wi-Fi operating mode.</param>
/// <param name="ConnectionType">Reported current connection type.</param>
/// <param name="SonosNetDisabled">Whether SonosNet is disabled, independently of current connection type.</param>
public sealed record WirelessObservationDto(string? WifiMode, string? ConnectionType, bool? SonosNetDisabled);
