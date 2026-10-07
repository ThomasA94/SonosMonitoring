using System.Text.Json;
using SonosMonitoring.Application.Collection;
using SonosMonitoring.Application.Collection.Diagnostics;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Unit.Tests.Collection.Diagnostics;

/// <summary>Checks freshness, rate resets, policy gating, and privacy for live diagnostic observations.</summary>
public sealed class LiveDiagnosticStateTests
{
    /// <summary>A sixty-second policy uses three intervals rather than the shorter global stale-after limit.</summary>
    /// <param name="ageSeconds">Age of the last successful diagnostic sample.</param>
    /// <param name="expectedStale">Whether the sample should be expired.</param>
    [Theory]
    [InlineData(59, false)]
    [InlineData(181, true)]
    public void FreshnessUsesEffectiveIntervalWhenItExceedsStaleAfter(int ageSeconds, bool expectedStale)
    {
        CollectorState state = new(new CollectorOptions
        {
            Enabled = true,
            StaleAfter = TimeSpan.FromSeconds(45),
            PollInterval = TimeSpan.FromSeconds(15),
        });
        DateTimeOffset observed = DateTimeOffset.UtcNow.AddSeconds(-ageSeconds);
        state.DescriptionSucceeded(new PlayerDescription("RINCON_LIVE", null, null), "192.168.8.20", observed);
        state.DiagnosticDecision("RINCON_LIVE", true, 60, DateTimeOffset.UtcNow);
        state.InterfaceSucceeded("RINCON_LIVE", [Counters("br0", 100, 200)], observed);
        state.WirelessSucceeded("RINCON_LIVE", new WirelessObservation("WiFi", "Ethernet", false), observed);
        state.EthernetSucceeded("RINCON_LIVE", [new EthernetPortObservation("00", false, null)], observed);

        DeviceSnapshot device = Assert.Single(state.GetSnapshot().Devices);

        Assert.Equal(expectedStale, device.InterfaceStatus.IsStale);
        Assert.Equal(expectedStale, device.WirelessStatus.IsStale);
        Assert.Equal(expectedStale, device.EthernetStatus.IsStale);
    }

    /// <summary>Rates match only equal interface labels and omit only the direction whose counter reset.</summary>
    [Fact]
    public void RateCalculatorKeepsInterfaceAndDirectionBoundaries()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        InterfaceCounters[] previous = [Counters("br0", 100, 400), Counters("eth0", 1000, 1000)];
        InterfaceCounters[] current = [Counters("br0", 160, 300), Counters("eth0", 1200, 1100)];

        IReadOnlyList<InterfaceRate> rates = InterfaceRateCalculator.Calculate(previous, now.AddSeconds(-10), current, now, 60);

        Assert.Equal(2, rates.Count);
        InterfaceRate bridge = Assert.Single(rates, rate => rate.Interface == "br0");
        Assert.Equal(6d, bridge.ReceiveBytesPerSecond);
        Assert.Null(bridge.TransmitBytesPerSecond);
        InterfaceRate ethernet = Assert.Single(rates, rate => rate.Interface == "eth0");
        Assert.Equal(20d, ethernet.ReceiveBytesPerSecond);
        Assert.Equal(10d, ethernet.TransmitBytesPerSecond);
    }

    /// <summary>A gap beyond the per-device freshness budget does not create a rate from unrelated periods.</summary>
    [Fact]
    public void RateCalculatorOmitsFirstSampleAndLongGaps()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        InterfaceCounters[] current = [Counters("br0", 200, 300)];

        Assert.Empty(InterfaceRateCalculator.Calculate([], null, current, now, 180));
        Assert.Empty(InterfaceRateCalculator.Calculate(current, now.AddSeconds(-181), current, now, 180));
    }

    /// <summary>Policy denial retains last good observations but removes rates and all new diagnostic samples.</summary>
    [Fact]
    public void PolicyDenialRetainsValuesAndHidesLiveDiagnosticMeasurements()
    {
        CollectorState state = new(new CollectorOptions { Enabled = true, StaleAfter = TimeSpan.FromSeconds(45) });
        DateTimeOffset first = DateTimeOffset.UtcNow.AddMinutes(-1);
        DateTimeOffset second = first.AddSeconds(10);
        state.DescriptionSucceeded(new PlayerDescription("RINCON_POLICY", null, null), "192.168.8.21", first);
        state.DiagnosticDecision("RINCON_POLICY", true, 60, first);
        state.InterfaceSucceeded("RINCON_POLICY", [Counters("br0", 100, 200)], first);
        state.InterfaceSucceeded("RINCON_POLICY", [Counters("br0", 200, 400)], second);
        state.WirelessSucceeded("RINCON_POLICY", new WirelessObservation("WiFi", "Ethernet", false), second);
        state.EthernetSucceeded("RINCON_POLICY", [new EthernetPortObservation("00", true, 1000)], second);
        state.PlaybackSucceeded("RINCON_POLICY", new PlaybackObservation("PLAYING", "OK"), second);
        state.VolumeSucceeded("RINCON_POLICY", 55, true, second);
        DeviceSnapshot beforeDenial = Assert.Single(state.GetSnapshot().Devices);
        Assert.Single(beforeDenial.InterfaceRates);

        state.DiagnosticDecision("RINCON_POLICY", false, 60, DateTimeOffset.UtcNow);
        DeviceSnapshot afterDenial = Assert.Single(state.GetSnapshot().Devices);
        string exposition = CollectorMetricsFormatter.Format(state.GetSnapshot());

        Assert.Equal(200UL, Assert.Single(afterDenial.Interfaces).ReceiveBytes);
        Assert.Empty(afterDenial.InterfaceRates);
        Assert.Equal("WiFi", afterDenial.Wireless?.WifiMode);
        Assert.Equal("00", Assert.Single(afterDenial.EthernetPorts).Port);
        Assert.Equal("PLAYING", afterDenial.Playback?.TransportState);
        Assert.Equal(55, afterDenial.Volume);
        Assert.True(afterDenial.Muted);
        Assert.DoesNotContain("sonos_interface_receive_bytes_total{", exposition);
        Assert.DoesNotContain("sonos_device_volume{", exposition);
        Assert.DoesNotContain("sonos_device_muted{", exposition);
        Assert.DoesNotContain("sonos_ethernet_link_up{", exposition);
        Assert.Equal("monitoring_disabled", afterDenial.InterfaceStatus.ErrorCode);
    }

    /// <summary>Redaction aliases new labels, reduces reported states, and preserves safe measurements.</summary>
    [Fact]
    public void RedactorSanitizesNewDiagnosticFieldsAndRetainsMeasurements()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SourceSnapshot current = new("volume", true, now, now, false, null);
        DeviceSnapshot device = new("SECRET_DEVICE", "192.168.8.22", "SECRET_ROOM", "SECRET_MODEL", now, now,
            new SourceSnapshot("description", true, now, now, false, null), current, [Counters("SECRET_INTERFACE", 1, 2)])
        {
            WirelessStatus = new("wireless", true, now, now, false, "SECRET_SOURCE_ERROR"),
            EthernetStatus = new("ethernet", true, now, now, false, null),
            PlaybackStatus = new("playback", true, now, now, false, null),
            VolumeStatus = new("volume", true, now, now, false, null),
            Wireless = new("SECRET_WIFI_MODE", "SECRET_CONNECTION", true),
            EthernetPorts = [new("SECRET_PORT", true, 1000)],
            Playback = new("SECRET_TRANSPORT_STATE", "SECRET_TRANSPORT_STATUS"),
            Volume = 63,
            Muted = true,
            ProbeDurationSeconds = 1.25,
            InterfaceRates = [new("SECRET_RATE_INTERFACE", 10, 20, now, 1)],
            Gena = new(true, true, now, now.AddMinutes(1), now, true, "SECRET_GENA_ERROR"),
        };
        CollectorSnapshot input = new(true, now, now, [device], [], false);

        CollectorSnapshot redacted = CollectorSnapshotRedactor.Redact(input);
        DeviceSnapshot safeDevice = Assert.Single(redacted.Devices);
        string json = JsonSerializer.Serialize(redacted);

        Assert.DoesNotContain("SECRET_", json, StringComparison.Ordinal);
        Assert.DoesNotContain("192.168.8.22", json, StringComparison.Ordinal);
        Assert.Equal(63, safeDevice.Volume);
        Assert.True(safeDevice.Muted);
        Assert.Equal(10d, Assert.Single(safeDevice.InterfaceRates).ReceiveBytesPerSecond);
        Assert.Equal("UNKNOWN", safeDevice.Wireless?.WifiMode);
        Assert.Equal("UNKNOWN", safeDevice.Playback?.TransportState);
        Assert.Equal("other_error", safeDevice.Gena.ErrorCode);
        Assert.Equal("other_error", safeDevice.WirelessStatus.ErrorCode);
        Assert.Equal(1.25, safeDevice.ProbeDurationSeconds);
        Assert.NotEqual("SECRET_PORT", Assert.Single(safeDevice.EthernetPorts).Port);
    }

    /// <summary>Unobserved fields stay absent instead of being represented as successful zero measurements.</summary>
    [Fact]
    public void PendingSourcesDoNotInventNumericMeasurements()
    {
        CollectorState state = new(new CollectorOptions { Enabled = true });
        state.DescriptionSucceeded(new PlayerDescription("RINCON_PENDING", null, null), "192.168.8.23", DateTimeOffset.UtcNow);

        string exposition = CollectorMetricsFormatter.Format(state.GetSnapshot());

        Assert.DoesNotContain("sonos_device_volume{", exposition);
        Assert.DoesNotContain("sonos_device_muted{", exposition);
        Assert.DoesNotContain("sonos_device_probe_duration_seconds{", exposition);
        Assert.DoesNotContain("sonos_device_gena_last_notification_timestamp_seconds{", exposition);
        Assert.DoesNotContain("sonos_interface_receive_bytes_total{", exposition);
    }

    /// <summary>Creates a complete raw counter observation for the selected interface.</summary>
    /// <param name="name">Stable interface label.</param>
    /// <param name="receive">Receive bytes.</param>
    /// <param name="transmit">Transmit bytes.</param>
    /// <returns>Counter record with unrelated counters set to zero.</returns>
    private static InterfaceCounters Counters(string name, ulong receive, ulong transmit) =>
        new(name, receive, transmit, 0, 0, 0, 0, 0, 0);
}
