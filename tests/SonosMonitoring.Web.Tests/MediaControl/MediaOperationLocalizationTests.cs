using System.Globalization;
using SonosMonitoring.Web.Localization;

namespace SonosMonitoring.Web.Tests.MediaControl;

/// <summary>Checks both supported translations for partial media execution warnings.</summary>
public sealed class MediaOperationLocalizationTests
{
    /// <summary>Resolves the partial rejection warning in German and English.</summary>
    [Fact]
    public void Partial_rejection_warning_is_localized()
    {
        CultureInfo originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            UiText text = new(new BrowserTimeZone());
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Contains("Earlier acknowledged steps may still be in effect", text["MediaPartialRejected"]);

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Contains("Vorherige bestätigte Schritte bleiben möglicherweise wirksam", text["MediaPartialRejected"]);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }
}
