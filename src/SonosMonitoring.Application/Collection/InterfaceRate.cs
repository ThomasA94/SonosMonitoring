namespace SonosMonitoring.Application.Collection;

/// <summary>One reset-aware interface rate derived from two successful observations.</summary>
/// <param name="Interface">Interface whose counters were compared.</param>
/// <param name="ReceiveBytesPerSecond">Receive rate, or null after reset.</param>
/// <param name="TransmitBytesPerSecond">Transmit rate, or null after reset.</param>
/// <param name="SampledAtUtc">Time of the newer observation.</param>
/// <param name="IntervalSeconds">Actual time between observations.</param>
public sealed record InterfaceRate(string Interface, double? ReceiveBytesPerSecond, double? TransmitBytesPerSecond,
    DateTimeOffset SampledAtUtc, double IntervalSeconds);
