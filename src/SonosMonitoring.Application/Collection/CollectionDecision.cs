namespace SonosMonitoring.Application.Collection;

/// <summary>Current monitoring permission and desired diagnostic polling interval for one player.</summary>
/// <param name="Enabled">Whether diagnostic polling is allowed.</param>
/// <param name="PollInterval">Minimum interval requested for this player.</param>
public sealed record CollectionDecision(bool Enabled, TimeSpan PollInterval);
