using System.Text.Json;
using SonosMonitoring.Contracts.Collection;

namespace SonosMonitoring.Web.Snapshots;

/// <summary>Validates untrusted diagnostic files before rendering a detached offline view.</summary>
public static class SnapshotDocument
{
    /// <summary>Gets the maximum upload and parsing budget.</summary>
    public const int MAX_BYTES = 4 * 1024 * 1024;

    /// <summary>Reads the supported export shape without writing it to storage or contacting a player.</summary>
    /// <param name="stream">Bounded uploaded JSON stream.</param>
    /// <param name="ct">Page cancellation.</param>
    public static async Task<CollectorSnapshotExportDto> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        var document = await JsonSerializer.DeserializeAsync<CollectorSnapshotExportDto>(stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { MaxDepth = 32 }, ct);
        if (document is null || document.Format != "sonosmonitoring.collection-snapshot" || document.SchemaVersion != 1 ||
            document.Privacy != "reduced-identifiers-v1" || document.CapturedAtUtc == default || document.Snapshot is null ||
            document.Snapshot.Devices is null || document.Snapshot.Ranges is null || document.Snapshot.Devices.Count > 1024 ||
            document.Snapshot.Ssdp is null || document.Snapshot.TopologyDiscovery is null ||
            document.Snapshot.SweepDurationSeconds is double sweepDuration && (double.IsFinite(sweepDuration) == false || sweepDuration < 0) ||
            document.Snapshot.DiscoveryAddressesProbed < 0 || document.Snapshot.DiscoveryDevicesFound < 0 ||
            document.Snapshot.DiscoveryDevicesFound > document.Snapshot.DiscoveryAddressesProbed ||
            document.Snapshot.Ranges.Any(range => range is null) ||
            document.Snapshot.Devices.Any(device => device is null || string.IsNullOrWhiteSpace(device.DeviceId) ||
                device.Description is null || device.InterfaceStatus is null || device.TopologyStatus is null || device.Gena is null ||
                device.WirelessStatus is null || device.EthernetStatus is null || device.PlaybackStatus is null || device.VolumeStatus is null ||
                device.Interfaces is null || device.InterfaceRates is null || device.EthernetPorts is null ||
                device.Interfaces.Count > 64 || device.InterfaceRates.Count > 64 || device.EthernetPorts.Count > 32 ||
                device.Volume is < 0 or > 100 || device.EffectiveIntervalSeconds is double interval &&
                    (double.IsFinite(interval) == false || interval <= 0 || interval > 86400) ||
                device.ProbeDurationSeconds is double duration && (double.IsFinite(duration) == false || duration < 0) ||
                device.InterfaceRates.Any(rate => rate is null || string.IsNullOrWhiteSpace(rate.Interface) ||
                    double.IsFinite(rate.IntervalSeconds) == false || rate.IntervalSeconds <= 0 ||
                    rate.ReceiveBytesPerSecond is double receive && (double.IsFinite(receive) == false || receive < 0) ||
                    rate.TransmitBytesPerSecond is double transmit && (double.IsFinite(transmit) == false || transmit < 0)) ||
                device.EthernetPorts.Any(port => port is null || string.IsNullOrWhiteSpace(port.Port) || port.ReportedSpeed is < 0) ||
                device.Interfaces.Any(entry => entry is null) || device.Groups is null ||
                device.Groups.Any(group => group is null || group.Members is null || group.Members.Any(member => member is null))) ||
            document.Snapshot.Devices.Select(device => device.DeviceId).Distinct(StringComparer.Ordinal).Count() != document.Snapshot.Devices.Count)
        {
            throw new JsonException("Unsupported diagnostic document.");
        }
        return document;
    }
}
