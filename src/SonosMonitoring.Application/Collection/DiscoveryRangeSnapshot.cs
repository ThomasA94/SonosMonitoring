namespace SonosMonitoring.Application.Collection;

/// <summary>Progress and result of the latest bounded scan of a configured range.</summary>
/// <param name="Cidr">Canonical IPv4 search range.</param>
/// <param name="LastAttemptUtc">Latest scan attempt.</param>
/// <param name="LastCompletedUtc">Latest completed scan.</param>
/// <param name="LastAttemptSucceeded">Whether the latest scan completed within its deadline.</param>
/// <param name="AddressesProbed">Addresses attempted in the latest scan.</param>
/// <param name="DevicesFound">Validated players observed in the latest scan.</param>
/// <param name="ErrorCode">Bounded failure category, if any.</param>
public sealed record DiscoveryRangeSnapshot(
    string Cidr,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastCompletedUtc,
    bool LastAttemptSucceeded,
    int AddressesProbed,
    int DevicesFound,
    string? ErrorCode);
