namespace SonosMonitoring.Application.Collection;

/// <summary>Calculates per-interface rates without joining unrelated counters or bridging collection gaps.</summary>
public static class InterfaceRateCalculator
{
    /// <summary>Compares consecutive samples only within the allowed measurement window.</summary>
    /// <param name="previous">Previous successful counters.</param>
    /// <param name="previousTime">Previous successful observation time.</param>
    /// <param name="current">New successful counters.</param>
    /// <param name="now">New observation time.</param>
    /// <param name="maximumGapSeconds">Largest permitted interval.</param>
    /// <returns>Rates with null values for reset counters; empty before a second sample or after a gap.</returns>
    public static IReadOnlyList<InterfaceRate> Calculate(IReadOnlyList<InterfaceCounters> previous,
        DateTimeOffset? previousTime, IReadOnlyList<InterfaceCounters> current, DateTimeOffset now, double maximumGapSeconds)
    {
        double seconds = previousTime is null ? 0 : (now - previousTime.Value).TotalSeconds;
        if (seconds <= 0 || seconds > maximumGapSeconds)
        {
            return [];
        }
        return current.Select(item =>
        {
            InterfaceCounters? old = previous.FirstOrDefault(entry => entry.Interface == item.Interface);
            return old is null ? null : new InterfaceRate(item.Interface,
                Rate(old.ReceiveBytes, item.ReceiveBytes, seconds), Rate(old.TransmitBytes, item.TransmitBytes, seconds), now, seconds);
        }).OfType<InterfaceRate>().ToArray();
    }

    /// <summary>Returns a nonnegative rate, omitting a decreased counter instead of guessing reset timing.</summary>
    /// <param name="previous">Older raw counter.</param>
    /// <param name="current">Newer raw counter.</param>
    /// <param name="seconds">Positive actual interval.</param>
    private static double? Rate(ulong previous, ulong current, double seconds) =>
        current < previous ? null : (current - previous) / seconds;
}
