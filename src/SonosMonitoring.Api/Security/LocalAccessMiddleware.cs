using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace SonosMonitoring.Api.Security;

/// <summary>Restricts the initial backend to loopback unless an explicit API key authorizes a LAN caller.</summary>
public sealed class LocalAccessMiddleware
{
    private readonly RequestDelegate _next;
    private readonly byte[]? _expectedKey;

    /// <summary>Captures the optional deployment secret once; no secret is logged.</summary>
    /// <param name="next">Next middleware.</param>
    /// <param name="configuration">Deployment settings.</param>
    public LocalAccessMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        this._next = next;
        var key = configuration["Access:ApiKey"];
        if (key is not null && key.Length < 32)
        {
            throw new InvalidOperationException("Access:ApiKey must contain at least 32 characters.");
        }
        this._expectedKey = key is null ? null : SHA256.HashData(Encoding.UTF8.GetBytes(key));
    }

    /// <summary>Allows loopback without a key only in the default local mode.</summary>
    /// <param name="context">Incoming request; forwarded headers are deliberately not trusted.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var local = context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
        var supplied = context.Request.Headers["X-Api-Key"].ToString();
        var authorized = this._expectedKey is null ? local :
            CryptographicOperations.FixedTimeEquals(this._expectedKey, SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
        if (authorized == false)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { code = "access.denied" });
            return;
        }
        await this._next(context);
    }
}
