using SonosMonitoring.Application.MediaControl;

namespace SonosMonitoring.Infrastructure.MediaControl;

/// <summary>Selects ready queue entries that do not overlap active or newly selected targets.</summary>
internal static class MediaExecutionScheduler
{
    /// <summary>Finds up to the available capacity in durable queue order.</summary>
    /// <param name="pending">Pending operations in durable order.</param>
    /// <param name="activeTargets">Target sets already reserved by running operations.</param>
    /// <param name="activeIds">Operation identities already running.</param>
    /// <param name="capacity">Number of additional operations that can start.</param>
    /// <param name="isReadyAsync">Database check for earlier or uncertain overlapping operations.</param>
    /// <param name="ct">Executor ownership lifetime.</param>
    /// <returns>Ready operations with pairwise-disjoint target sets.</returns>
    public static async Task<IReadOnlyList<CoordinatedMediaOperation>> SelectAsync(
        IReadOnlyList<CoordinatedMediaOperation> pending,
        IEnumerable<string[]> activeTargets,
        IReadOnlySet<Guid> activeIds,
        int capacity,
        Func<CoordinatedMediaOperation, CancellationToken, Task<bool>> isReadyAsync,
        CancellationToken ct)
    {
        if (capacity <= 0)
        {
            return [];
        }

        List<HashSet<string>> reservedTargets = activeTargets
            .Select(targets => new HashSet<string>(targets, StringComparer.Ordinal))
            .ToList();
        List<CoordinatedMediaOperation> selected = [];
        foreach (CoordinatedMediaOperation operation in pending)
        {
            if (selected.Count >= capacity)
            {
                break;
            }
            if (activeIds.Contains(operation.OperationId) || Overlaps(operation.Targets, reservedTargets))
            {
                continue;
            }
            if (operation.ExpiresAtUtc > DateTimeOffset.UtcNow && await isReadyAsync(operation, ct) == false)
            {
                continue;
            }

            selected.Add(operation);
            reservedTargets.Add(new HashSet<string>(operation.Targets, StringComparer.Ordinal));
        }

        return selected;
    }

    /// <summary>Checks a candidate against all current reservations.</summary>
    /// <param name="targets">Candidate target set.</param>
    /// <param name="reservedTargets">Active and selected target sets.</param>
    /// <returns>True when at least one target is already reserved.</returns>
    private static bool Overlaps(string[] targets, IEnumerable<HashSet<string>> reservedTargets)
    {
        HashSet<string> candidate = new(targets, StringComparer.Ordinal);
        return reservedTargets.Any(reserved => reserved.Overlaps(candidate));
    }
}
