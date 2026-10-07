using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;

namespace SonosMonitoring.Web.Tests.Security;

/// <summary>Exercises the actual login and antiforgery middleware using an isolated HTTP listener.</summary>
public sealed class SessionJourneyTests
{
    /// <summary>Anonymous pages and circuits are blocked; login and language changes require a valid antiforgery token.</summary>
    [Fact]
    public async Task Login_language_logout_and_antiforgery_are_enforced_over_http()
    {
        string secret = new('x', 48);
        await using WebApplication app = SonosMonitoring.Web.WebHost.Build([], new Dictionary<string, string?>
        {
            ["Web:AccessKey"] = secret,
            ["Api:BaseUrl"] = "http://127.0.0.1:1/",
            ["AllowedHosts"] = "*",
            ["Logging:LogLevel:Default"] = "Error",
        });
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) };
        using var denied = await client.GetAsync("/snapshot/download");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/login", denied.Headers.Location!.OriginalString);
        using var circuit = await client.PostAsync("/_blazor/negotiate?negotiateVersion=1", new StringContent(""));
        Assert.Equal(HttpStatusCode.Redirect, circuit.StatusCode);
        using var csrf = await client.PostAsync("/session", new FormUrlEncodedContent(new Dictionary<string, string> { ["key"] = secret }));
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        Assert.DoesNotContain(handler.CookieContainer.GetAllCookies().Cast<Cookie>(), cookie => cookie.Name == "SonosMonitoring.Session");

        string login = await client.GetStringAsync("/login");
        Assert.DoesNotContain(secret, login);
        string token = ExtractToken(login);
        using var signedIn = await client.PostAsync("/session", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["key"] = secret,
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        var cookie = Assert.Single(handler.CookieContainer.GetAllCookies().Cast<Cookie>(), cookie => cookie.Name == "SonosMonitoring.Session");
        Assert.True(cookie.HttpOnly);
        string page = await client.GetStringAsync("/snapshots");
        string authenticatedToken = ExtractToken(page);
        using var change = await client.PostAsync("/culture", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["culture"] = "en",
            ["returnUrl"] = "https://untrusted.invalid/",
            ["__RequestVerificationToken"] = authenticatedToken,
        }));
        Assert.Equal("/", change.Headers.Location!.OriginalString);
        Assert.Contains("lang=\"en\"", await client.GetStringAsync("/snapshots"));
        using var logout = await client.PostAsync("/logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = authenticatedToken }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        using var after = await client.GetAsync("/snapshots");
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        await app.StopAsync();
    }

    /// <summary>Extracts the generated antiforgery field exactly as a browser would submit it.</summary>
    /// <param name="html">Server-rendered form HTML.</param>
    private static string ExtractToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Expected a rendered antiforgery form field.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
