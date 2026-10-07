namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>A bounded received UDP payload and its transport-level sender address.</summary>
/// <param name="Payload">Complete response bytes.</param>
/// <param name="SenderAddress">Literal sender address obtained from the socket.</param>
public sealed record SsdpDatagram(byte[] Payload, string SenderAddress);
