using System.Net;

namespace SonosMonitoring.Web.Security;

/// <summary>Prevents a server-side API client from turning the UI into an unauthenticated LAN proxy.</summary>
public sealed class WebAccessMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Captures the next request delegate.</summary>
    /// <param name="next">Remaining HTTP pipeline.</param>
    public WebAccessMiddleware(RequestDelegate next) => this._next = next;

    /// <summary>Protects pages, downloads and circuits using the actual peer, never untrusted forwarding headers.</summary>
    /// <param name="context">Current browser request.</param>
    /// <param name="options">Validated installation access configuration.</param>
    public async Task InvokeAsync(HttpContext context, WebAccessOptions options)
    {
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "same-origin";
        context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'self'; object-src 'none'; base-uri 'self'";
        bool local = context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
        if ((options.RequiresLogin == false && local == false) ||
            (options.RequiresLogin && local == false && context.Request.IsHttps == false))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        string path = context.Request.Path.Value ?? "/";
        bool publicPage = path is "/login" or "/session" or "/culture" or "/error" or "/app.css" or "/app.js" ||
            path.StartsWith("/_content/", StringComparison.Ordinal) || path.StartsWith("/_framework/", StringComparison.Ordinal);
        if (options.RequiresLogin && context.User.Identity?.IsAuthenticated != true && publicPage == false)
        {
            context.Response.Redirect("/login");
            return;
        }
        await this._next(context);
    }
}
