namespace SonosMonitoring.Web.Localization;

/// <summary>Converts instants and local form values using the current browser's named time zone.</summary>
public sealed class BrowserTimeZone
{
    private TimeZoneInfo _zone = TimeZoneInfo.Utc;

    /// <summary>Gets the active zone identifier, with UTC used until browser discovery succeeds.</summary>
    public string Id => this._zone.Id;

    /// <summary>Gets whether the browser supplied a supported zone.</summary>
    public bool IsDetected { get; private set; }

    /// <summary>Accepts a browser zone and keeps a visible UTC fallback for unavailable zone data.</summary>
    /// <param name="id">IANA time zone identifier supplied by the browser.</param>
    public void Set(string? id)
    {
        this._zone = TimeZoneInfo.Utc;
        this.IsDetected = false;
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        try
        {
            this._zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            this.IsDetected = true;
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            // Unknown client zones must never silently select the server's local zone.
        }
    }

    /// <summary>Converts an instant with the offset that applied at that instant, including historical DST.</summary>
    /// <param name="value">Absolute source timestamp.</param>
    public DateTimeOffset Convert(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, this._zone);

    /// <summary>Converts a local form value to UTC, rejecting missing or repeated clock-change times.</summary>
    /// <param name="value">Wall-clock input whose DateTime kind is deliberately ignored.</param>
    /// <param name="utc">Unique UTC instant when conversion succeeds.</param>
    /// <returns>Whether the input identifies one unambiguous instant in the browser zone.</returns>
    public bool TryToUtc(DateTime value, out DateTimeOffset utc)
    {
        DateTime local = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        utc = default;
        if (this._zone.IsInvalidTime(local) || this._zone.IsAmbiguousTime(local))
        {
            return false;
        }

        utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, this._zone));
        return true;
    }
}
