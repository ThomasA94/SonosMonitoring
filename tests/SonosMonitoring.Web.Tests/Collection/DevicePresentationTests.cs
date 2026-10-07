using SonosMonitoring.Contracts.Collection;
using SonosMonitoring.Web.Components.Collection;
using SonosMonitoring.Web.Localization;

namespace SonosMonitoring.Web.Tests.Collection;

/// <summary>Guards deliberate source states and representative rates against misleading health or traffic claims.</summary>
public sealed class DevicePresentationTests
{
    /// <summary>Deliberate diagnostic states take precedence over failed or absent requests.</summary>
    /// <param name="code">Bounded backend reason.</param>
    /// <param name="expected">Presentation category.</param>
    [Theory]
    [InlineData("monitoring_disabled", "MonitoringDisabled")]
    [InlineData("policy_unavailable", "PolicyUnavailable")]
    [InlineData("unsupported", "Unsupported")]
    [InlineData("not_supported", "Unsupported")]
    [InlineData("diagnostics_not_due", "DiagnosticsNotDue")]
    public void Deliberate_states_are_not_reported_as_transport_failures(string code, string expected)
    {
        Assert.Equal(expected, DevicePresentation.Status(new("ifconfig", false, null, null, true, code)));
    }

    /// <summary>Bridge rates are displayed once rather than added to overlapping physical interface traffic.</summary>
    [Fact]
    public void Bridge_is_preferred_and_reset_stays_unknown()
    {
        var source = new SourceSnapshotDto("description", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false, null);
        var device = new DeviceSnapshotDto("TEST", "192.0.2.1", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, source, source, [])
        {
            InterfaceRates = [new("lo", 500, 500, DateTimeOffset.UtcNow, 15), new("eth0", 100, 200, DateTimeOffset.UtcNow, 15), new("br0", null, 200, DateTimeOffset.UtcNow, 15)],
        };
        Assert.Equal("br0", DevicePresentation.Rate(device)!.Interface);
        Assert.Null(DevicePresentation.Rate(device)!.ReceiveBytesPerSecond);
        Assert.Equal(new UiText(new BrowserTimeZone())["Unknown"], DevicePresentation.RateValue(null, new UiText(new BrowserTimeZone())));
        Assert.Equal("eth0", DevicePresentation.Rate(device with { InterfaceRates = device.InterfaceRates.Take(2).ToArray() })!.Interface);
        Assert.Null(DevicePresentation.Rate(device with { InterfaceRates = device.InterfaceRates.Take(1).ToArray() }));
    }

    /// <summary>Non-finite and negative values cannot be rendered as valid traffic.</summary>
    /// <param name="value">Invalid derived rate.</param>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    public void Invalid_rates_remain_unknown(double value)
    {
        Assert.Equal(new UiText(new BrowserTimeZone())["Unknown"], DevicePresentation.RateValue(value, new UiText(new BrowserTimeZone())));
    }
}
