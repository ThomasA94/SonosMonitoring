using SonosMonitoring.Application.Collection;
using SonosMonitoring.Application.History;

namespace SonosMonitoring.Infrastructure.History;

/// <summary>Removes polling timestamps, counters, and ordering noise from comparison evidence.</summary>
public static class ObservationNormalizer
{
    /// <summary>Produces a deterministic state while preserving the newest source attempt separately.</summary>
    /// <param name="device">One detached reporter snapshot.</param>
    public static ObservationSample Normalize(DeviceSnapshot device)
    {
        var groups = device.Groups.OrderBy(group => group.GroupId, StringComparer.Ordinal)
            .ThenBy(group => group.CoordinatorDeviceId, StringComparer.Ordinal)
            .Select(group => new ObservedGroup(group.GroupId, group.CoordinatorDeviceId,
                group.Members.OrderBy(member => member.DeviceId, StringComparer.Ordinal)
                    .ThenBy(member => member.ParentDeviceId, StringComparer.Ordinal)
                    .Select(member => new ObservedMember(member.DeviceId, member.ParentDeviceId)).ToArray())).ToArray();
        var attempt = new[] { device.Description.LastAttemptUtc, device.TopologyStatus.LastAttemptUtc }.Max();
        return new ObservationSample(new(device.DeviceId, device.Address, Quality(device.Description),
            Quality(device.TopologyStatus), groups), attempt);
    }

    /// <summary>Distinguishes failed attempts from unavailable or aged evidence.</summary>
    /// <param name="source">Current source status.</param>
    private static string Quality(SourceSnapshot source) => source.LastAttemptUtc is null ? "unobserved" :
        source.LastAttemptSucceeded == false ? "failed" : source.IsStale ? "stale" : "available";
}
