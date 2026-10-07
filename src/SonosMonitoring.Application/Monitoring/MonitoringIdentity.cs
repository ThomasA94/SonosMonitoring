using System.Security.Cryptography;
using System.Text;
using SonosMonitoring.Domain.Monitoring;

namespace SonosMonitoring.Application.Monitoring;

/// <summary>Provides a stable policy stream for a stable Sonos UDN across retries and IP changes.</summary>
public static class MonitoringIdentity
{
    /// <summary>Derives a namespaced deterministic identifier; changing this algorithm requires migration.</summary>
    /// <param name="deviceKey">Validated canonical device key.</param>
    public static DeviceMonitoringId ForDevice(string deviceKey)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes("SonosMonitoring/device/v1/" + deviceKey));
        return DeviceMonitoringId.With(new Guid(digest.AsSpan(0, 16)));
    }
}
