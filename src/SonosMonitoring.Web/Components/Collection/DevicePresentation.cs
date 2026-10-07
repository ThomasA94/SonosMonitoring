using SonosMonitoring.Contracts.Collection;
using SonosMonitoring.Web.Localization;

namespace SonosMonitoring.Web.Components.Collection;

/// <summary>Formats observations consistently without inferring network paths or combining interface traffic.</summary>
public static class DevicePresentation
{
    /// <summary>Classifies deliberate collection states before transport quality.</summary>
    /// <param name="source">Independent source observation.</param>
    public static string Status(SourceSnapshotDto source) => source.ErrorCode switch
    {
        "monitoring_disabled" => "MonitoringDisabled",
        "policy_unavailable" => "PolicyUnavailable",
        "unsupported" or "not_supported" => "Unsupported",
        "diagnostics_not_due" => "DiagnosticsNotDue",
        _ => source.LastAttemptUtc is null ? "Unobserved" : source.LastAttemptSucceeded == false ? "Failed" : source.IsStale ? "Stale" : "Available",
    };

    /// <summary>Selects a representative interface without adding bridge and physical counters.</summary>
    /// <param name="device">Player snapshot containing backend calculated rates.</param>
    public static InterfaceRateDto? Rate(DeviceSnapshotDto device) => device.InterfaceRates.FirstOrDefault(rate => rate.Interface == "br0") ??
        device.InterfaceRates.FirstOrDefault(rate => rate.Interface != "lo");

    /// <summary>Formats a source's distinct state and age since its last good observation.</summary>
    /// <param name="source">Source observation.</param>
    /// <param name="text">Active UI resources.</param>
    public static string Source(SourceSnapshotDto source, UiText text) => text[Status(source)] + " \u00B7 " + text["DataAge"] + ": " +
        (source.LastSuccessUtc is { } success ? text.Number(Math.Max(0, (DateTimeOffset.UtcNow - success).TotalSeconds)) + " " + text["Seconds"] : text["Unknown"]);

    /// <summary>Formats missing and invalid rates as unknown instead of zero traffic.</summary>
    /// <param name="rate">Backend calculated reset-safe rate.</param>
    /// <param name="text">Active UI resources.</param>
    public static string RateValue(double? rate, UiText text) => rate is { } value && double.IsFinite(value) && value >= 0 ? text.Number(value) + " B/s" : text["Unknown"];

    /// <summary>Provides the same concise live context to canvas nodes and accessible device summaries.</summary>
    /// <param name="device">Player snapshot.</param>
    /// <param name="text">Active UI resources.</param>
    /// <param name="includeAge">Whether to include detailed source ages outside the compact graph labels.</param>
    public static IReadOnlyList<string> Lines(DeviceSnapshotDto device, UiText text, bool includeAge = true)
    {
        var lines = new List<string>
        {
            text["DescriptionSource"] + ": " + (includeAge ? Source(device.Description, text) : text[Status(device.Description)]),
            text["InterfaceSource"] + ": " + (includeAge ? Source(device.InterfaceStatus, text) : text[Status(device.InterfaceStatus)]),
            text["TopologySource"] + ": " + (includeAge ? Source(device.TopologyStatus, text) : text[Status(device.TopologyStatus)]),
            "GENA: " + text[device.Gena.Enabled ? device.Gena.Subscribed ? "Subscribed" : "Unobserved" : "Disabled"],
        };
        if (device.Wireless is { } wireless)
        { lines.Add(text["ConnectionType"] + ": " + text.Observation(wireless.ConnectionType)); }
        if (device.Playback is { } playback)
        { lines.Add(text["Playback"] + ": " + text.Observation(playback.TransportState)); }
        if (Rate(device) is { } rate)
        {
            lines.Add(rate.Interface + " \u00B7 RX " + RateValue(rate.ReceiveBytesPerSecond, text) + " \u00B7 TX " + RateValue(rate.TransmitBytesPerSecond, text));
        }
        return lines;
    }
}
