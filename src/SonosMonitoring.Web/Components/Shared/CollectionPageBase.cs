using SonosMonitoring.Contracts.Collection;

namespace SonosMonitoring.Web.Components.Shared;

/// <summary>Retains the last collector snapshot while surfacing failed refreshes independently.</summary>
public abstract class CollectionPageBase : LivePageBase
{
    /// <summary>Gets the last successfully decoded observation, or null before first success.</summary>
    protected CollectorSnapshotDto? Snapshot { get; private set; }
    /// <summary>Gets the time the UI last received a complete collector response.</summary>
    protected DateTimeOffset? ReceivedAt { get; private set; }

    /// <inheritdoc />
    protected override async Task LoadAsync()
    {
        var result = await this.Api.GetAsync<CollectorSnapshotDto>("api/collection", this.Lifetime);
        this.Error = result.ErrorCode;
        if (result.IsSuccess)
        {
            this.Snapshot = result.Value;
            this.ReceivedAt = DateTimeOffset.UtcNow;
        }
    }
}
