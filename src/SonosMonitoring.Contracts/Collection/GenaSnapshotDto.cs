namespace SonosMonitoring.Contracts.Collection;

/// <summary>Public event-path status; no callback tokens or subscription identifiers are exposed.</summary>
/// <param name="Enabled">Whether events are configured.</param>
/// <param name="Subscribed">Whether a current lease exists.</param>
/// <param name="LastAttemptUtc">Latest subscription attempt.</param>
/// <param name="ExpiresUtc">Granted finite expiry, or null.</param>
/// <param name="LastNotificationUtc">Last accepted notification time.</param>
/// <param name="InitialNotificationReceived">Whether the current lease has demonstrated its callback path.</param>
/// <param name="ErrorCode">Bounded status category.</param>
public sealed record GenaSnapshotDto(bool Enabled, bool Subscribed, DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? ExpiresUtc, DateTimeOffset? LastNotificationUtc, bool InitialNotificationReceived, string? ErrorCode);
