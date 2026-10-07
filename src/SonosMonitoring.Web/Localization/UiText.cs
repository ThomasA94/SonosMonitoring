using System.Globalization;
using System.Resources;

namespace SonosMonitoring.Web.Localization;

/// <summary>Resolves central bilingual resources using the selected request and circuit culture.</summary>
public sealed class UiText
{
    private static readonly ResourceManager _resources = new("SonosMonitoring.Web.Localization.Strings", typeof(UiText).Assembly) { IgnoreCase = true };
    private readonly BrowserTimeZone _timeZone;

    /// <summary>Uses the browser zone scoped to the current request or interactive circuit.</summary>
    /// <param name="timeZone">Browser-specific clock conversion state.</param>
    public UiText(BrowserTimeZone timeZone)
    {
        this._timeZone = timeZone;
    }

    /// <summary>Gets a translated UI label; unknown technical codes remain visible for diagnosis.</summary>
    /// <param name="key">Resource identity.</param>
    public string this[string key] => _resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>Formats a timestamp in the browser zone and UI culture, including an explicit UTC offset.</summary>
    /// <param name="value">Optional source timestamp.</param>
    /// <param name="includeSeconds">Whether precise event timestamps should include seconds.</param>
    public string Time(DateTimeOffset? value, bool includeSeconds = false)
    {
        if (value is null)
        {
            return this["Unknown"];
        }

        DateTimeOffset local = this._timeZone.Convert(value.Value);
        return local.ToString(includeSeconds ? "G" : "g", CultureInfo.CurrentCulture) + " UTC" + local.ToString("zzz", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats numeric values consistently across tables and charts.</summary>
    /// <param name="value">Displayed measurement.</param>
    public string Number(double value) => value.ToString("N2", CultureInfo.CurrentCulture);

    /// <summary>Translates supported device states while retaining unknown firmware values for diagnosis.</summary>
    /// <param name="value">Optional reported state or mode.</param>
    public string Observation(string? value) => value is null ? this["Unknown"] :
        _resources.GetString("Observation_" + value, CultureInfo.CurrentUICulture) ?? value;

    /// <summary>Maps transport failures to actionable localized messages.</summary>
    /// <param name="code">Safe error code.</param>
    public string Error(string code) => code switch
    {
        "access.denied" => this["AccessDenied"],
        "backend.disabled" or "history.unconfigured" or "alerts.unconfigured" => this["Unconfigured"],
        "projection.pending" => this["ProjectionPending"],
        "request.conflict" => this["Conflict"],
        "monitoring.interval.invalid" => this["IntervalInvalid"],
        "history.query.invalid" => this["InvalidPeriod"],
        "history.local-time.invalid" => this["InvalidLocalTime"],
        "media.control.disabled" or "media.grant.revoked" => this["MediaDisabled"],
        "media.target.blocked" or "media.outcome.unknown" => this["MediaBlockedHint"],
        "media.target.changed" or "media.queue.changed" => this["MediaContextChanged"],
        "media.action.unsupported" => this["MediaUnsupported"],
        "media.device.unreachable" => this["MediaUnavailable"],
        "media.idempotency.conflict" => this["MediaIdempotencyConflict"],
        _ => this["RequestFailed"],
    };
}
