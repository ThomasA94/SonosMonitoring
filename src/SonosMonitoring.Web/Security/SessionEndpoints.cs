using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using SonosMonitoring.Web.Connection;

namespace SonosMonitoring.Web.Security;

/// <summary>Owns cookie login, logout, language preference and authenticated snapshot downloads.</summary>
public static class SessionEndpoints
{
    /// <summary>Maps small browser endpoints; every cookie-changing POST validates antiforgery.</summary>
    /// <param name="app">Configured web host.</param>
    public static void MapSessionEndpoints(this WebApplication app)
    {
        app.MapPost("/session", async (HttpContext context, IAntiforgery antiforgery, WebAccessOptions options) =>
        {
            if (await ValidateAsync(context, antiforgery) == false)
            {
                return Results.BadRequest(new { code = "access.csrf" });
            }
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            if (options.Matches(form["key"].ToString()) == false)
            {
                return Results.Redirect("/login?failed=true");
            }
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "installation")], CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.Redirect("/");
        }).RequireRateLimiting("login");
        app.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (await ValidateAsync(context, antiforgery) == false)
            {
                return Results.BadRequest(new { code = "access.csrf" });
            }
            await context.SignOutAsync();
            return Results.Redirect("/login");
        });
        app.MapPost("/culture", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (await ValidateAsync(context, antiforgery) == false)
            {
                return Results.BadRequest(new { code = "access.csrf" });
            }
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            string culture = form["culture"] == "en" ? "en" : "de";
            context.Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions { HttpOnly = true, IsEssential = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Strict, MaxAge = TimeSpan.FromDays(365) });
            string returnUrl = form["returnUrl"].ToString();
            return Results.LocalRedirect(returnUrl.StartsWith('/') && returnUrl.StartsWith("//", StringComparison.Ordinal) == false && returnUrl.Contains('\\') == false ? returnUrl : "/");
        });
        app.MapGet("/snapshot/download", async (MonitoringApiClient api, HttpContext context) =>
        {
            var result = await api.DownloadAsync(context.RequestAborted);
            return result.IsSuccess ? Results.File(result.Value!, "application/json", "sonosmonitoring-snapshot.json") :
                Results.Problem(statusCode: 503, title: "snapshot.unavailable");
        });
    }

    /// <summary>Rejects missing or stale form tokens without converting an expected denial into a server failure.</summary>
    /// <param name="context">Current form submission.</param>
    /// <param name="antiforgery">Framework token validator.</param>
    private static async Task<bool> ValidateAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
