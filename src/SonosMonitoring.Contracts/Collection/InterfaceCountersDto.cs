namespace SonosMonitoring.Contracts.Collection;

/// <summary>Transport view of raw interface counters without inferred rates.</summary>
/// <param name="Interface">Device interface name.</param>
/// <param name="ReceiveBytes">Raw received bytes.</param>
/// <param name="TransmitBytes">Raw transmitted bytes.</param>
/// <param name="ReceivePackets">Raw received packets.</param>
/// <param name="TransmitPackets">Raw transmitted packets.</param>
/// <param name="ReceiveErrors">Raw receive errors.</param>
/// <param name="TransmitErrors">Raw transmit errors.</param>
/// <param name="ReceiveDropped">Raw receive drops.</param>
/// <param name="TransmitDropped">Raw transmit drops.</param>
public sealed record InterfaceCountersDto(
    string Interface,
    ulong ReceiveBytes,
    ulong TransmitBytes,
    ulong ReceivePackets,
    ulong TransmitPackets,
    ulong ReceiveErrors,
    ulong TransmitErrors,
    ulong ReceiveDropped,
    ulong TransmitDropped);
