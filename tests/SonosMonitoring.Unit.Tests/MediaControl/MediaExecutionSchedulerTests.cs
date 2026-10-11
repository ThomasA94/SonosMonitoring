using SonosMonitoring.Application.MediaControl;
using SonosMonitoring.Infrastructure.MediaControl;

namespace SonosMonitoring.Unit.Tests.MediaControl;

/// <summary>Verifies bounded selection for operations that can safely run in parallel.</summary>
public sealed class MediaExecutionSchedulerTests
{
    /// <summary>Admits a disjoint operation while a slow target remains reserved and preserves target exclusivity.</summary>
    [Fact]
    public async Task SelectAsyncStartsDisjointWorkAndSkipsOverlappingTargets()
    {
        CoordinatedMediaOperation duplicate = this.Operation(1, "player-a");
        CoordinatedMediaOperation overlap = this.Operation(2, "player-a");
        CoordinatedMediaOperation disjoint = this.Operation(3, "player-c");
        CoordinatedMediaOperation overlapWithSelected = this.Operation(4, "player-c", "player-d");
        CoordinatedMediaOperation nextDisjoint = this.Operation(5, "player-e");
        List<Guid> readinessChecks = [];

        IReadOnlyList<CoordinatedMediaOperation> selected = await MediaExecutionScheduler.SelectAsync(
            new[] { duplicate, overlap, disjoint, overlapWithSelected, nextDisjoint },
            new[] { new[] { "player-a" } },
            new HashSet<Guid> { duplicate.OperationId },
            capacity: 2,
            (operation, _) =>
            {
                readinessChecks.Add(operation.OperationId);
                return Task.FromResult(true);
            },
            CancellationToken.None);

        Guid[] expectedSelected = [disjoint.OperationId, nextDisjoint.OperationId];
        Guid[] expectedChecks = [disjoint.OperationId, nextDisjoint.OperationId];
        Assert.Equal(expectedSelected, selected.Select(operation => operation.OperationId));
        Assert.Equal(expectedChecks, readinessChecks);
    }

    /// <summary>Skips a database-blocked queue head while allowing a later independent target to proceed.</summary>
    [Fact]
    public async Task SelectAsyncContinuesPastDatabaseBlockedOperation()
    {
        CoordinatedMediaOperation blocked = this.Operation(1, "player-a");
        CoordinatedMediaOperation ready = this.Operation(2, "player-b");

        IReadOnlyList<CoordinatedMediaOperation> selected = await MediaExecutionScheduler.SelectAsync(
            new[] { blocked, ready },
            Array.Empty<string[]>(),
            new HashSet<Guid>(),
            capacity: 1,
            (operation, _) => Task.FromResult(operation.OperationId == ready.OperationId),
            CancellationToken.None);

        Assert.Equal(new[] { ready }, selected);
    }

    /// <summary>Allows expired work to reach the executor so it can be terminalized without dispatch.</summary>
    [Fact]
    public async Task SelectAsyncAdmitsExpiredWorkEvenWhenDatabaseReadinessIsBlocked()
    {
        CoordinatedMediaOperation expired = this.Operation(1, "player-a") with
        {
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
        };
        int readinessChecks = 0;

        IReadOnlyList<CoordinatedMediaOperation> selected = await MediaExecutionScheduler.SelectAsync(
            new[] { expired },
            Array.Empty<string[]>(),
            new HashSet<Guid>(),
            capacity: 1,
            (_, _) =>
            {
                readinessChecks++;
                return Task.FromResult(false);
            },
            CancellationToken.None);

        Assert.Equal(new[] { expired }, selected);
        Assert.Equal(0, readinessChecks);
    }

    /// <summary>Creates a minimal immutable operation for target selection.</summary>
    /// <param name="position">Durable queue position.</param>
    /// <param name="targets">Targets reserved by the operation.</param>
    /// <returns>An admitted operation descriptor.</returns>
    private CoordinatedMediaOperation Operation(long position, params string[] targets) => new(
        Guid.Parse($"00000000-0000-0000-0000-{position:D12}"),
        position,
        $"fingerprint-{position}",
        "00000000-0000-0000-0000-000000000001",
        targets,
        "{}",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddMinutes(1));
}
