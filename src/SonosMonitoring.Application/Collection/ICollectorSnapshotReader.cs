namespace SonosMonitoring.Application.Collection;

/// <summary>Exposes an immutable view of the latest local observations.</summary>
public interface ICollectorSnapshotReader
{
    /// <summary>Gets a point-in-time snapshot with freshness calculated at read time.</summary>
    /// <returns>The current collector state.</returns>
    CollectorSnapshot GetSnapshot();
}
