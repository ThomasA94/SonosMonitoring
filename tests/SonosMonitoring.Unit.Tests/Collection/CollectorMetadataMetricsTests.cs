using SonosMonitoring.Application.Collection;
using SonosMonitoring.Application.Collection.Diagnostics;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Verifies readable metadata, stable measurement identities and unavailable-state omission.</summary>
public sealed class CollectorMetadataMetricsTests
{
    /// <summary>Room renaming updates only metadata and preserves counter labels and numerical values.</summary>
    [Fact]
    public void RenamingDoesNotForkCounterIdentity()
    {
        DeviceSnapshot device = Player("RINCON_FIRST", "Office");
        string before = Scrape(device);
        string after = Scrape(device with { RoomName = "Study" });
        Assert.Contains("device_name=\"Office\"", before);
        Assert.Contains("device_name=\"Study\"", after);
        string counter = before.Split('\n').Single(line => line.StartsWith("sonos_interface_receive_bytes_total{", StringComparison.Ordinal));
        Assert.Contains(counter, after);
        Assert.DoesNotContain("device_name", counter);
        Assert.DoesNotContain("RINCON_FIRST", after);
        Assert.DoesNotContain(device.Address, after);
    }

    /// <summary>Repeated names receive short disambiguators without becoming raw hardware identifiers.</summary>
    [Fact]
    public void DuplicateAndMissingRoomNamesRemainReadable()
    {
        string scrape = Scrape(Player("RINCON_FIRST", "Office"), Player("RINCON_SECOND", "Office"),
            Player("RINCON_THIRD", null) with { ModelName = "Sonos One" },
            Player("RINCON_FOURTH", null) with { ModelName = null });
        Assert.Contains($"device_name=\"Office ({CollectorMetricsFormatter.DeviceLabel("RINCON_FIRST")[..8]})\"", scrape);
        Assert.Contains($"device_name=\"Office ({CollectorMetricsFormatter.DeviceLabel("RINCON_SECOND")[..8]})\"", scrape);
        Assert.Contains("device_name=\"Sonos One\"", scrape);
        Assert.Contains("device_name=\"Sonos player\"", scrape);
    }

    /// <summary>Quotes, backslashes, controls and very long Unicode labels cannot corrupt the scrape.</summary>
    [Fact]
    public void BoundsAndEscapesNamesIncludingDuplicateSuffixes()
    {
        string escaped = Scrape(Player("RINCON_FIRST", "Study \"A\"\\\r\n\t"));
        Assert.Contains("device_name=\"Study \\\"A\\\"\\\\\"", escaped);
        string name = string.Concat(Enumerable.Repeat("\U0001F3B5", 200));
        string scrape = Scrape(Player("RINCON_FIRST", name), Player("RINCON_SECOND", name));
        Assert.Contains($" ({CollectorMetricsFormatter.DeviceLabel("RINCON_FIRST")[..8]})\"", scrape);
        Assert.DoesNotContain(string.Concat(Enumerable.Repeat("\U0001F3B5", 129)), scrape);
        Assert.DoesNotContain('\uFFFD', scrape);
    }

    /// <summary>Current playback and wireless metadata disappear after a failed or stale observation.</summary>
    /// <param name="failed">Whether the latest attempt failed instead of merely expiring.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SourceFailureDoesNotExportRetainedStatesAsCurrent(bool failed)
    {
        DeviceSnapshot device = Player("RINCON_FIRST", "Office") with
        {
            Wireless = new WirelessObservation("STATION_MODE", "WiFi", false),
            Playback = new PlaybackObservation("PLAYING", "OK")
        };
        Assert.Contains("sonos_wireless_info{", Scrape(device));
        Assert.Contains("sonos_playback_info{", Scrape(device));
        SourceSnapshot unavailable = device.WirelessStatus with
        {
            LastAttemptSucceeded = failed == false,
            IsStale = failed == false,
            ErrorCode = failed ? "timeout" : null
        };
        string scrape = Scrape(device with { WirelessStatus = unavailable, PlaybackStatus = unavailable with { Name = "playback" } });
        Assert.DoesNotContain("sonos_wireless_info{", scrape);
        Assert.DoesNotContain("sonos_playback_info{", scrape);
        Assert.Contains("sonos_device_info{", scrape);
    }

    /// <summary>Creates a valid current player snapshot without touching real collector state.</summary>
    /// <param name="id">Stable synthetic identifier.</param>
    /// <param name="room">Optional display name.</param>
    /// <returns>Current player with a counter and independent current source evidence.</returns>
    private static DeviceSnapshot Player(string id, string? room)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SourceSnapshot current = new("description", true, now, now, false, null);
        return new DeviceSnapshot(id, "192.0.2.1", room, "Sonos", now, now, current,
            current with { Name = "ifconfig" }, [new InterfaceCounters("br0", 42, 9, 3, 4, 0, 0, 0, 0)])
        {
            WirelessStatus = current with { Name = "wireless" },
            PlaybackStatus = current with { Name = "playback" }
        };
    }

    /// <summary>Formats synthetic snapshots using the production exporter.</summary>
    /// <param name="devices">Synthetic player observations.</param>
    /// <returns>Prometheus text from the actual production formatter.</returns>
    private static string Scrape(params DeviceSnapshot[] devices) =>
        CollectorMetricsFormatter.Format(new CollectorSnapshot(true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, devices, [], false));
}
