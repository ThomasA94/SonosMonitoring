namespace SonosMonitoring.Application.Collection.Diagnostics;

/// <summary>Contains a reported Ethernet port state without assuming a speed unit.</summary>
/// <param name="Port">The device-reported port identifier.</param>
/// <param name="LinkUp">Whether the reported link is active.</param>
/// <param name="ReportedSpeed">The raw reported speed integer, or <see langword="null"/> when unavailable.</param>
public sealed record EthernetPortObservation(string Port, bool LinkUp, int? ReportedSpeed);
