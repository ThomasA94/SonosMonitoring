namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Owns the common startup and interactive configuration validation.</summary>
public static class CollectorConfigurationValidator
{
    /// <summary>Validates all target and resource boundaries without starting network work.</summary>
    /// <param name="options">Candidate settings.</param>
    /// <returns>The validated target policy.</returns>
    public static Ipv4TargetPolicy Validate(CollectorOptions options)
    {
        if (options.Gena is null || options.AllowedCidrs is null || options.SeedAddresses is null || options.SsdpInterfaceAddresses is null ||
            options.AllowedCidrs.Count > 64 || options.SeedAddresses.Count > 1024 ||
            options.AllowedCidrs.Concat(options.SeedAddresses).Concat(options.SsdpInterfaceAddresses).Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 64) ||
            options.RequestTimeout > TimeSpan.FromMinutes(1) || options.SweepTimeout > TimeSpan.FromMinutes(10) ||
            options.PollInterval > TimeSpan.FromDays(1) || options.DiscoveryInterval > TimeSpan.FromDays(7) || options.StaleAfter > TimeSpan.FromDays(7))
        {
            throw new ArgumentException("Collector settings contain invalid values.", nameof(options));
        }
        if (options.MaxScanAddresses < 1 || options.MaxScanAddresses > 65536 ||
            options.MaxKnownDevices < 1 || options.MaxKnownDevices > options.MaxScanAddresses ||
            options.MaxTopologyCandidates < 1 || options.MaxTopologyCandidates > 256 ||
            options.MaxParallelism < 1 || options.MaxParallelism > 64 ||
            options.MaxResponseBytes < 1024 || options.MaxResponseBytes > 1024 * 1024 ||
            options.RequestTimeout <= TimeSpan.Zero || options.SweepTimeout <= TimeSpan.Zero ||
            options.PollInterval < TimeSpan.FromSeconds(1) ||
            options.DiscoveryInterval < options.PollInterval || options.StaleAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Collector limits are invalid.");
        }

        Ipv4TargetPolicy policy = new(options.AllowedCidrs, options.AllowLoopback);
        SsdpConfiguration.Validate(options);
        Gena.GenaConfiguration.Validate(options);
        if (options.Enabled && policy.Ranges.Count == 0)
        {
            throw new ArgumentException("Enabled collection requires at least one allowed CIDR.", nameof(options));
        }

        foreach (string seed in options.SeedAddresses)
        {
            if (policy.IsAllowed(seed) == false)
            {
                throw new ArgumentException("A seed address is outside the allowed CIDRs.", nameof(options));
            }
        }

        HashSet<string> targets = new(options.SeedAddresses, StringComparer.Ordinal);
        foreach (Ipv4Range range in policy.Ranges)
        {
            foreach (string address in range.EnumerateHosts(options.MaxScanAddresses))
            {
                targets.Add(address);
                if (targets.Count > options.MaxScanAddresses)
                {
                    throw new ArgumentException("Configured targets exceed MaxScanAddresses.", nameof(options));
                }
            }
        }


        return policy;
    }
}
