namespace SonosMonitoring.Contracts.Monitoring;

/// <summary>A durable commit acknowledgement; projection visibility must be checked separately.</summary>
/// <param name="AggregateId">Stable stream identity.</param>
/// <param name="ConcurrencyToken">Token for the next independent action.</param>
/// <param name="EventSequence">Position that the read model must reach.</param>
/// <param name="AlreadyCommitted">Whether this receipt belongs to a previous retry.</param>
/// <param name="QueryUrl">Location for observing projection progress.</param>
public sealed record MonitoringAccepted(string AggregateId, string ConcurrencyToken, long EventSequence, bool AlreadyCommitted, string QueryUrl);
