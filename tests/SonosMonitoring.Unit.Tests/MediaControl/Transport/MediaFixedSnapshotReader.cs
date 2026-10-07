using SonosMonitoring.Application.Collection;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Unit.Tests.MediaControl.Transport;

/// <summary>Provides a detached collector snapshot for a simulated player.</summary>
/// <param name="snapshot">Current fixed collector evidence.</param>
internal sealed class MediaFixedSnapshotReader(CollectorSnapshot snapshot) : ICollectorSnapshotReader
{
    /// <inheritdoc />
    public CollectorSnapshot GetSnapshot() => snapshot;
}
