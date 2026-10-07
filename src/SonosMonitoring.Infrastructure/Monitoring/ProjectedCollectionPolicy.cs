using SonosMonitoring.Application.Collection;
using SonosMonitoring.Application.Monitoring;

namespace SonosMonitoring.Infrastructure.Monitoring;

/// <summary>Enforces the confirmed projected policy at every diagnostic scheduling decision.</summary>
public sealed class ProjectedCollectionPolicy : IDeviceCollectionPolicy
{
    private readonly IMonitoringQuery _query;

    /// <summary>Receives the read port shared with the policy HTTP view.</summary>
    /// <param name="query">Confirmed, eventually consistent policy projection.</param>
    public ProjectedCollectionPolicy(IMonitoringQuery query) => this._query = query;

    /// <inheritdoc />
    public async Task<CollectionDecision> GetDecisionAsync(string deviceId, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        var policy = await this._query.FindAsync(deviceId, budget.Token);
        return new CollectionDecision(policy?.Enabled ?? false, TimeSpan.FromSeconds(policy?.IntervalSeconds ?? 15));
    }
}
