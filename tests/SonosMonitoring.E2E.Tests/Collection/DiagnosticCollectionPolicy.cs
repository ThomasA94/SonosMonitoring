using SonosMonitoring.Application.Collection;

namespace SonosMonitoring.E2E.Tests.Collection;

/// <summary>Creates a short-lived policy for the isolated loopback collection host.</summary>
internal sealed class DiagnosticCollectionPolicy : IDeviceCollectionPolicy
{
    private readonly bool _enabled;
    private readonly int _intervalSeconds;

    /// <summary>Initializes one consistent policy decision.</summary>
    /// <param name="enabled">Whether collection is allowed.</param>
    /// <param name="intervalSeconds">The configured device interval.</param>
    public DiagnosticCollectionPolicy(bool enabled, int intervalSeconds)
    {
        this._enabled = enabled;
        this._intervalSeconds = intervalSeconds;
    }

    /// <summary>Returns this fixture's confirmed scheduling decision.</summary>
    /// <param name="deviceId">The discovered player identity.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The configured decision.</returns>
    public Task<CollectionDecision> GetDecisionAsync(string deviceId, CancellationToken cancellationToken) =>
        Task.FromResult(new CollectionDecision(this._enabled, TimeSpan.FromSeconds(this._intervalSeconds)));
}
