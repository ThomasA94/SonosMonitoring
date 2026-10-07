using SonosMonitoring.Web.Localization;

namespace SonosMonitoring.Web.Tests.Localization;

/// <summary>Verifies browser clock isolation, seasonal offsets and unambiguous metric range conversion.</summary>
public sealed class BrowserTimeZoneTests
{
    /// <summary>Unknown browser zones preserve UTC rather than inheriting the host's zone.</summary>
    [Fact]
    public void Missing_or_unknown_zone_uses_utc_and_marks_fallback()
    {
        var zone = new BrowserTimeZone();
        Assert.False(zone.IsDetected);
        Assert.Equal("UTC", zone.Id);
        zone.Set("Europe/Berlin");
        zone.Set("invalid/browser-zone");
        Assert.False(zone.IsDetected);
        Assert.Equal(TimeSpan.Zero, zone.Convert(DateTimeOffset.UtcNow).Offset);
        Assert.True(zone.TryToUtc(new DateTime(2026, 10, 1, 12, 0, 0), out var utc));
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), utc);
    }

    /// <summary>Historical timestamps use the applicable seasonal offset and a separate circuit remains isolated.</summary>
    [Fact]
    public void Browser_zone_uses_each_instant_offset_without_affecting_other_browsers()
    {
        var berlin = new BrowserTimeZone();
        berlin.Set("Europe/Berlin");
        var newYork = new BrowserTimeZone();
        newYork.Set("America/New_York");
        Assert.True(berlin.IsDetected);
        var winter = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var summer = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromHours(1), berlin.Convert(winter).Offset);
        Assert.Equal(TimeSpan.FromHours(2), berlin.Convert(summer).Offset);
        Assert.Equal(TimeSpan.FromHours(-5), newYork.Convert(winter).Offset);
        Assert.Equal(TimeSpan.FromHours(-4), newYork.Convert(summer).Offset);
        Assert.EndsWith("UTC+02:00", new UiText(berlin).Time(summer));
        Assert.EndsWith("UTC-04:00", new UiText(newYork).Time(summer));
    }

    /// <summary>Repeated wall-clock times remain distinguishable in displayed history by their UTC offsets.</summary>
    [Fact]
    public void Repeated_hour_displays_distinct_offsets()
    {
        var zone = new BrowserTimeZone();
        zone.Set("Europe/Berlin");
        var text = new UiText(zone);
        var first = new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero);
        Assert.EndsWith("UTC+02:00", text.Time(first));
        Assert.EndsWith("UTC+01:00", text.Time(first.AddHours(1)));
    }

    /// <summary>Custom input conversion uses the offset of the selected date rather than today's offset.</summary>
    /// <param name="month">Requested month.</param>
    /// <param name="expectedHour">Expected UTC hour.</param>
    [Theory]
    [InlineData(1, 11)]
    [InlineData(7, 10)]
    public void Custom_input_converts_using_the_selected_dates_offset(int month, int expectedHour)
    {
        var zone = new BrowserTimeZone();
        zone.Set("Europe/Berlin");
        Assert.True(zone.TryToUtc(new DateTime(2026, month, 15, 12, 0, 0), out var utc));
        Assert.Equal(new DateTimeOffset(2026, month, 15, expectedHour, 0, 0, TimeSpan.Zero), utc);
    }

    /// <summary>Clock changes cannot silently shift or reinterpret a user's custom time range.</summary>
    /// <param name="month">Clock-change month.</param>
    /// <param name="day">Clock-change day.</param>
    [Theory]
    [InlineData(3, 29)]
    [InlineData(10, 25)]
    public void Missing_and_repeated_custom_times_are_rejected(int month, int day)
    {
        var zone = new BrowserTimeZone();
        zone.Set("Europe/Berlin");
        Assert.False(zone.TryToUtc(new DateTime(2026, month, day, 2, 30, 0), out _));
    }
}
