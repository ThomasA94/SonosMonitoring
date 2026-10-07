namespace SonosMonitoring.Application.Collection;

/// <summary>Supplies the current application-owned monitoring decision for a discovered player.</summary>
public interface IDeviceCollectionPolicy
{
    /// <summary>Gets whether diagnostics may be collected and at which interval.</summary>
    /// <param name="deviceId">Stable player UDN without the UUID prefix.</param>
    /// <param name="cancellationToken">Collection cancellation.</param>
    /// <returns>The current decision.</returns>
    Task<CollectionDecision> GetDecisionAsync(string deviceId, CancellationToken cancellationToken);
}
