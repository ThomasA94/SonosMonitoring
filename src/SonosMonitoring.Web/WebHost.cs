using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using MudBlazor.Services;
using SonosMonitoring.Web.Components;
using SonosMonitoring.Web.Connection;
using SonosMonitoring.Web.Localization;
using SonosMonitoring.Web.Security;
using SonosMonitoring.Observability.Diagnostics;

namespace SonosMonitoring.Web;

/// <summary>Composes a standalone frontend whose only data boundary is the backend HTTP API.</summary>
public static class WebHost
{
    /// <summary>Builds the UI host with optional isolated test configuration.</summary>
    /// <param name="args">Host command-line settings.</param>
    /// <param name="overrides">Optional configuration overrides for isolated tests.</param>
    public static WebApplication Build(string[] args, IDictionary<string, string?>? overrides = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(WebHost).Assembly.GetName().Name,
        });
        builder.WebHost.UseStaticWebAssets();
        if (overrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(overrides);
            if (overrides.ContainsKey("ApplicationLogs:Directory") == false && overrides.ContainsKey("ApplicationLogs:Enabled") == false)
            {
                builder.Configuration["ApplicationLogs:Enabled"] = "false";
            }
        }
        builder.Logging.AddApplicationLogs(builder.Configuration, "web");
        var address = builder.Configuration["Api:BaseUrl"] ?? "http://127.0.0.1:48080/";
        if (Uri.TryCreate(address, UriKind.Absolute, out var apiUri) == false ||
            apiUri.Scheme is not ("http" or "https") || apiUri.UserInfo.Length > 0 || apiUri.Query.Length > 0 || apiUri.Fragment.Length > 0)
        {
            throw new InvalidOperationException("Api:BaseUrl must be an absolute HTTP(S) URL without credentials, query or fragment.");
        }
        builder.Services.AddSingleton(new WebAccessOptions(builder.Configuration["Web:AccessKey"]));
        builder.Services.AddHttpClient<MonitoringApiClient>(client =>
        {
            client.BaseAddress = apiUri;
            client.Timeout = TimeSpan.FromSeconds(15);
            client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
            if (builder.Configuration["Api:ApiKey"] is { } key)
            {
                client.DefaultRequestHeaders.Add("X-Api-Key", key);
            }
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.AddLocalization();
        builder.Services.AddScoped<UiText>();
        builder.Services.AddScoped<BrowserTimeZone>();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddMudServices();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.LoginPath = "/login";
            options.Cookie.Name = "SonosMonitoring.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
        });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });
        var app = builder.Build();
        if (app.Environment.IsDevelopment() == false)
        {
            app.UseExceptionHandler("/error");
        }
        app.UseRequestLocalization(new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture("de"),
            SupportedCultures = [new CultureInfo("de"), new CultureInfo("en")],
            SupportedUICultures = [new CultureInfo("de"), new CultureInfo("en")],
            RequestCultureProviders = [new CookieRequestCultureProvider(), new AcceptLanguageHeaderRequestCultureProvider()],
        });
        app.UseAuthentication();
        app.UseMiddleware<WebAccessMiddleware>();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseAntiforgery();
        app.MapSessionEndpoints();
        app.MapStaticAssets();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode().Add(endpoint =>
        {
            var dispatcher = endpoint.Metadata.OfType<Microsoft.AspNetCore.Http.Connections.HttpConnectionDispatcherOptions>().FirstOrDefault();
            if (dispatcher is not null)
            {
                dispatcher.CloseOnAuthenticationExpiration = true;
            }
        });
        return app;
    }
}
