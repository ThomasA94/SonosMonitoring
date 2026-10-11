namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Builds a fair, bounded order for known-player polling and full-range discovery.</summary>
internal sealed class DiscoveryTargetScheduler
{
    private int _cursor;

    /// <summary>Interleaves and deduplicates known targets with a rotated sequence of range targets.</summary>
    /// <param name="knownTargets">Seed, retained-player, SSDP, and topology candidates.</param>
    /// <param name="rangeTargets">The deduplicated, round-robin range host sequence.</param>
    /// <returns>Targets in the order that a bounded sweep should probe them.</returns>
    public IReadOnlyList<DiscoveryTarget> CreatePlan(IReadOnlyList<string> knownTargets,
        IReadOnlyList<(string Cidr, string Address)> rangeTargets)
    {
        string[] known = knownTargets.Distinct(StringComparer.Ordinal).ToArray();
        (string Cidr, string Address)[] discovery = rangeTargets
            .DistinctBy(item => item.Address, StringComparer.Ordinal).ToArray();
        Dictionary<string, string> rangeByAddress = discovery.ToDictionary(item => item.Address, item => item.Cidr, StringComparer.Ordinal);
        List<(string Address, string? Cidr)> ordered = new(known.Length + discovery.Length);
        HashSet<string> scheduled = new(StringComparer.Ordinal);
        int knownIndex = 0;
        int discoveryIndex = 0;
        bool takeDiscovery = true;

        while (knownIndex < known.Length || discoveryIndex < discovery.Length)
        {
            if ((takeDiscovery && discoveryIndex < discovery.Length) || knownIndex >= known.Length)
            {
                (string cidr, string address) = discovery[discoveryIndex++];
                if (scheduled.Add(address))
                {
                    ordered.Add((address, cidr));
                }
            }
            else
            {
                string address = known[knownIndex++];
                if (scheduled.Add(address))
                {
                    ordered.Add((address, rangeByAddress.GetValueOrDefault(address)));
                }
            }

            takeDiscovery = takeDiscovery == false;
        }

        if (ordered.Count == 0)
        {
            return [];
        }

        int start = this._cursor % ordered.Count;
        List<DiscoveryTarget> plan = new(ordered.Count);
        for (int offset = 0; offset < ordered.Count; offset++)
        {
            int sourceIndex = (start + offset) % ordered.Count;
            (string address, string? cidr) = ordered[sourceIndex];
            plan.Add(new DiscoveryTarget(address, cidr, (sourceIndex + 1) % ordered.Count));
        }

        return plan;
    }

    /// <summary>Moves to the exact next entry after the last target whose probe started.</summary>
    /// <param name="plan">The rotated plan returned by <see cref="CreatePlan"/>.</param>
    /// <param name="startedTargetCount">Number of initial plan entries whose probes started.</param>
    public void RecordStarted(IReadOnlyList<DiscoveryTarget> plan, int startedTargetCount)
    {
        int count = Math.Clamp(startedTargetCount, 0, plan.Count);
        if (count > 0)
        {
            this._cursor = plan[count - 1].NextCursor;
        }
    }
}
