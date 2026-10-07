namespace SonosMonitoring.Contracts.Collection;

/// <summary>Reported Ethernet port state with an explicitly unverified speed unit.</summary>
/// <param name="Port">Device-reported port number.</param>
/// <param name="LinkUp">Whether link is reported active.</param>
/// <param name="ReportedSpeed">Raw reported speed; no Mbps interpretation is promised.</param>
public sealed record EthernetPortObservationDto(string Port, bool LinkUp, int? ReportedSpeed);
