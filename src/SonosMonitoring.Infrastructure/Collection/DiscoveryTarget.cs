namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>One deduplicated target, its optional range attribution, and its next schedule position.</summary>
/// <param name="Address">Literal IPv4 address to probe.</param>
/// <param name="Cidr">Owning range for range targets, or null for known-only targets.</param>
/// <param name="NextCursor">Index immediately after this target in the unrotated schedule.</param>
internal sealed record DiscoveryTarget(string Address, string? Cidr, int NextCursor);
