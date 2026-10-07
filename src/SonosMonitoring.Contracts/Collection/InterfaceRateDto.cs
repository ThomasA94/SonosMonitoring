namespace SonosMonitoring.Contracts.Collection;

/// <summary>Reset-aware measured interval rate for one interface.</summary>
/// <param name="Interface">Interface name.</param>
/// <param name="ReceiveBytesPerSecond">Receive bytes/s or null after reset.</param>
/// <param name="TransmitBytesPerSecond">Transmit bytes/s or null after reset.</param>
/// <param name="SampledAtUtc">Newest sample time.</param>
/// <param name="IntervalSeconds">Actual sample interval.</param>
public sealed record InterfaceRateDto(string Interface, double? ReceiveBytesPerSecond, double? TransmitBytesPerSecond,
    DateTimeOffset SampledAtUtc, double IntervalSeconds);
