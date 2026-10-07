namespace SonosMonitoring.Contracts.Collection;

/// <summary>Explicit opt-in and network boundaries for a dedicated local GENA callback listener.</summary>
public sealed class GenaSettingsDto
{
    /// <summary>Gets or sets whether topology subscriptions are requested.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the local literal IPv4 address on which the callback listener binds.</summary>
    public string ListenAddress { get; set; } = "0.0.0.0";

    /// <summary>Gets or sets the literal IPv4 address advertised to players, which must be reachable from them.</summary>
    public string? CallbackAddress { get; set; }

    /// <summary>Gets or sets the dedicated callback port, separate from the protected API.</summary>
    public int Port { get; set; } = 48081;

    /// <summary>Gets or sets the requested lease length; device-granted expiry remains authoritative.</summary>
    public int RequestedLeaseSeconds { get; set; } = 1800;

    /// <summary>Gets or sets the wait for an initial notification before reporting an unconfirmed callback route.</summary>
    public TimeSpan InitialNotificationTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets or sets the minimum delay between event-triggered reconciliation reads per player.</summary>
    public TimeSpan MinimumPollInterval { get; set; } = TimeSpan.FromSeconds(2);
}
