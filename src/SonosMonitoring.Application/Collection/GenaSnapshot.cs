namespace SonosMonitoring.Application.Collection;

/// <summary>Event-path evidence, independent of HTTP reachability and polling source quality.</summary>
/// <param name="Enabled">Whether event reception is configured.</param>
/// <param name="Subscribed">Whether a current lease exists.</param>
/// <param name="LastAttemptUtc">Latest subscribe or renewal attempt.</param>
/// <param name="ExpiresUtc">Granted lease expiry; null for no lease or an infinite lease.</param>
/// <param name="LastNotificationUtc">Last accepted notification, possibly from a prior lease.</param>
/// <param name="InitialNotificationReceived">Whether this lease's return path has been demonstrated.</param>
/// <param name="ErrorCode">Bounded event-path status without subscription secrets.</param>
public sealed record GenaSnapshot(bool Enabled, bool Subscribed, DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? ExpiresUtc, DateTimeOffset? LastNotificationUtc, bool InitialNotificationReceived, string? ErrorCode);
