using Microsoft.Extensions.Logging;
using SonosMonitoring.Application.Collection;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Produces bounded network evidence from completed discovery and known-player state transitions.</summary>
public static class CollectorNetworkDiagnostics
{
    /// <summary>Logs only lost and restored reachability, using the already bounded player cache instead of retaining scan targets.</summary>
    /// <param name="logger">Application diagnostic destination.</param>
    /// <param name="previous">Known players before the pass.</param>
    /// <param name="current">Known players after the pass.</param>
    public static void LogTransitions(ILogger logger, IReadOnlyList<DeviceSnapshot> previous, IReadOnlyList<DeviceSnapshot> current)
    {
        Dictionary<string, DeviceSnapshot> prior = previous.ToDictionary(device => device.DeviceId, StringComparer.Ordinal);
        foreach (DeviceSnapshot device in current)
        {
            if (prior.TryGetValue(device.DeviceId, out DeviceSnapshot? old) == false ||
                old.Description.LastAttemptSucceeded == device.Description.LastAttemptSucceeded)
            {
                continue;
            }

            if (device.Description.LastAttemptSucceeded)
            {
                logger.LogInformation("Sonos player {DeviceId} at {Address} is reachable again.", device.DeviceId, device.Address);
            }
            else
            {
                logger.LogWarning("Sonos player {DeviceId} at {Address} is no longer reachable: {ErrorCode}. Check player power, LAN access, and container routes; a Docker/LAN subnet overlap is a possible cause, not a confirmed diagnosis.",
                    device.DeviceId, device.Address, device.Description.ErrorCode);
            }
        }
    }

    /// <summary>Summarizes one full discovery without logging expected failures for each unused address.</summary>
    /// <param name="logger">Application diagnostic destination.</param>
    /// <param name="ranges">Validated CIDRs included in the sweep.</param>
    /// <param name="targets">Deduplicated scheduled address count.</param>
    /// <param name="probed">Completed identity attempts, including timed-out attempts.</param>
    /// <param name="found">Validated Sonos descriptions, independent of diagnostic success.</param>
    /// <param name="timeouts">Identity attempts that timed out.</param>
    /// <param name="deadlineExceeded">Whether the shared sweep deadline interrupted the pass.</param>
    public static void LogDiscovery(ILogger logger, IReadOnlyList<string> ranges, int targets, int probed, int found, int timeouts, bool deadlineExceeded)
    {
        LogLevel level = found == 0 || deadlineExceeded ? LogLevel.Warning : LogLevel.Information;
        string guidance = found == 0
            ? " No players found. Check configured CIDRs, player power, LAN access, and container routes. A Docker/LAN subnet overlap is a possible cause, not a confirmed diagnosis."
            : string.Empty;
        logger.Log(level,
            "Sonos discovery: {RangeCount} ranges [{Cidrs}], {TargetCount} scheduled, {ProbedCount} probed, {FoundCount} found, {FailureCount} failed (including {TimeoutCount} timeouts), {PendingCount} unprobed, sweep deadline exceeded: {DeadlineExceeded}.{Guidance}",
            ranges.Count, string.Join(", ", ranges), targets, probed, found, probed - found, timeouts, targets - probed, deadlineExceeded, guidance);
    }
}
