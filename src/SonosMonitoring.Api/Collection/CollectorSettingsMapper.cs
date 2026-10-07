using SonosMonitoring.Contracts.Collection;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Api.Collection;

/// <summary>Maps the public technical settings without exposing the testing-only loopback switch.</summary>
public static class CollectorSettingsMapper
{
    /// <summary>Builds a detached transport document.</summary>
    /// <param name="value">Validated collector options.</param>
    public static CollectorSettingsDto Map(CollectorOptions value) => new()
    {
        Enabled = value.Enabled,
        AllowedCidrs = value.AllowedCidrs,
        SeedAddresses = value.SeedAddresses,
        SsdpEnabled = value.SsdpEnabled,
        SsdpInterfaceAddresses = value.SsdpInterfaceAddresses,
        SsdpSearchWindow = value.SsdpSearchWindow,
        SsdpMaxDatagrams = value.SsdpMaxDatagrams,
        PollInterval = value.PollInterval,
        DiscoveryInterval = value.DiscoveryInterval,
        StaleAfter = value.StaleAfter,
        MaxScanAddresses = value.MaxScanAddresses,
        MaxKnownDevices = value.MaxKnownDevices,
        MaxTopologyCandidates = value.MaxTopologyCandidates,
        MaxParallelism = value.MaxParallelism,
        RequestTimeout = value.RequestTimeout,
        SweepTimeout = value.SweepTimeout,
        MaxResponseBytes = value.MaxResponseBytes,
        Gena = new() { Enabled = value.Gena.Enabled, ListenAddress = value.Gena.ListenAddress, CallbackAddress = value.Gena.CallbackAddress, Port = value.Gena.Port, RequestedLeaseSeconds = value.Gena.RequestedLeaseSeconds, InitialNotificationTimeout = value.Gena.InitialNotificationTimeout, MinimumPollInterval = value.Gena.MinimumPollInterval }
    };

    /// <summary>Builds options using only the host's loopback permission.</summary>
    /// <param name="value">Untrusted settings to validate before activation.</param>
    /// <param name="allowLoopback">Immutable deployment test permission.</param>
    public static CollectorOptions Map(CollectorSettingsDto value, bool allowLoopback) => new()
    {
        AllowLoopback = allowLoopback,
        Enabled = value.Enabled,
        AllowedCidrs = value.AllowedCidrs,
        SeedAddresses = value.SeedAddresses,
        SsdpEnabled = value.SsdpEnabled,
        SsdpInterfaceAddresses = value.SsdpInterfaceAddresses,
        SsdpSearchWindow = value.SsdpSearchWindow,
        SsdpMaxDatagrams = value.SsdpMaxDatagrams,
        PollInterval = value.PollInterval,
        DiscoveryInterval = value.DiscoveryInterval,
        StaleAfter = value.StaleAfter,
        MaxScanAddresses = value.MaxScanAddresses,
        MaxKnownDevices = value.MaxKnownDevices,
        MaxTopologyCandidates = value.MaxTopologyCandidates,
        MaxParallelism = value.MaxParallelism,
        RequestTimeout = value.RequestTimeout,
        SweepTimeout = value.SweepTimeout,
        MaxResponseBytes = value.MaxResponseBytes,
        Gena = new() { Enabled = value.Gena.Enabled, ListenAddress = value.Gena.ListenAddress, CallbackAddress = value.Gena.CallbackAddress, Port = value.Gena.Port, RequestedLeaseSeconds = value.Gena.RequestedLeaseSeconds, InitialNotificationTimeout = value.Gena.InitialNotificationTimeout, MinimumPollInterval = value.Gena.MinimumPollInterval }
    };
}
