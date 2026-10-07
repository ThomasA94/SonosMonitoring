namespace SonosMonitoring.Contracts.Collection;

/// <summary>Transport view of one configured IPv4 search range.</summary>
/// <param name="Cidr">Canonical configured range.</param>
/// <param name="LastAttemptUtc">Latest scan attempt.</param>
/// <param name="LastCompletedUtc">Latest completed scan.</param>
/// <param name="LastAttemptSucceeded">Whether the latest scan finished within its deadline.</param>
/// <param name="AddressesProbed">Addresses attempted in the latest scan.</param>
/// <param name="DevicesFound">Validated players returned in the latest scan.</param>
/// <param name="ErrorCode">Bounded scan failure code, if any.</param>
public sealed record DiscoveryRangeSnapshotDto(
    string Cidr,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastCompletedUtc,
    bool LastAttemptSucceeded,
    int AddressesProbed,
    int DevicesFound,
    string? ErrorCode);
