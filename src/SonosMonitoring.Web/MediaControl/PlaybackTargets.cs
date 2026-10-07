using SonosMonitoring.Contracts.Collection;

namespace SonosMonitoring.Web.MediaControl;

/// <summary>Builds room choices from each device's own topology without merging reporter claims.</summary>
public static class PlaybackTargets
{
    /// <summary>Finds the single group containing the reporter itself, leaving ambiguous evidence unknown.</summary>
    /// <param name="device">Observed device and its own topology.</param>
    /// <returns>Its own group or null when missing or ambiguous.</returns>
    public static TopologyGroupDto? OwnGroup(DeviceSnapshotDto device)
    {
        var groups = device.Groups.Where(group => group.Members.Any(member => member.DeviceId == device.DeviceId)).ToArray();
        return groups.Length == 1 ? groups[0] : null;
    }

    /// <summary>Lists visible rooms; this presentation filter never authorizes a device write.</summary>
    /// <param name="devices">Observed players.</param>
    /// <returns>Rooms ordered by label and stable identity.</returns>
    public static IEnumerable<DeviceSnapshotDto> Rooms(IReadOnlyList<DeviceSnapshotDto> devices) =>
        devices.Where(device => OwnGroup(device)?.Members.Any(member => member.DeviceId == device.DeviceId &&
            member.IsInvisible != true && member.ParentDeviceId is null) == true)
            .OrderBy(device => device.RoomName ?? device.DeviceId, StringComparer.CurrentCulture)
            .ThenBy(device => device.DeviceId, StringComparer.Ordinal);

    /// <summary>Suggests standalone rooms with successful recent collector evidence for a fresh API check.</summary>
    /// <param name="devices">Observed players.</param>
    /// <param name="members">Members already in the selected playback group.</param>
    /// <returns>Standalone room candidates; household and capabilities are validated by the API.</returns>
    public static IEnumerable<DeviceSnapshotDto> Additions(IReadOnlyList<DeviceSnapshotDto> devices, IReadOnlyList<string> members) =>
        Rooms(devices).Where(device => members.Contains(device.DeviceId) == false &&
            device.Description.LastAttemptSucceeded && device.Description.IsStale == false &&
            device.TopologyStatus.LastAttemptSucceeded && device.TopologyStatus.IsStale == false &&
            OwnGroup(device) is { Members.Count: 1 } group && group.CoordinatorDeviceId == device.DeviceId);
}
