using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SonosMonitoring.Web.Security;

namespace SonosMonitoring.Web.Tests.Security;

/// <summary>Checks that browser access cannot inherit the trusted web host's backend privileges.</summary>
public sealed class WebAccessTests
{
    /// <summary>Only the real loopback peer can use the unconfigured development mode.</summary>
    /// <param name="peer">Actual peer address, independent of forwarding headers.</param>
    /// <param name="expected">Whether the protected delegate may run.</param>
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("192.0.2.1", false)]
    public async Task Local_mode_checks_the_actual_peer(string peer, bool expected)
    {
        bool called = false;
        var middleware = new WebAccessMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
        await middleware.InvokeAsync(context, new WebAccessOptions(null));
        Assert.Equal(expected, called);
        if (expected == false)
        {
            Assert.Equal(403, context.Response.StatusCode);
        }
    }

    /// <summary>Configured login applies on loopback too, and LAN HTTP never accepts a session.</summary>
    /// <param name="peer">Actual remote address.</param>
    /// <param name="scheme">Transport scheme reported by Kestrel.</param>
    /// <param name="authenticated">Whether the cookie middleware established an identity.</param>
    /// <param name="status">Expected access outcome.</param>
    [Theory]
    [InlineData("127.0.0.1", "http", false, 302)]
    [InlineData("192.0.2.1", "http", true, 403)]
    [InlineData("192.0.2.1", "https", false, 302)]
    [InlineData("192.0.2.1", "https", true, 200)]
    public async Task Shared_key_mode_requires_an_authenticated_secure_lan_session(string peer, string scheme, bool authenticated, int status)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Scheme = scheme;
        context.Request.Path = "/_blazor";
        if (authenticated)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "installation")], "Cookies"));
        }
        var middleware = new WebAccessMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, new WebAccessOptions(new string('a', 32)));
        Assert.Equal(status, context.Response.StatusCode);
    }

    /// <summary>Missing secrets select local mode; malformed configured secrets fail startup.</summary>
    [Fact]
    public void Secret_validation_is_explicit()
    {
        Assert.Throws<InvalidOperationException>(() => new WebAccessOptions("short"));
        var options = new WebAccessOptions(new string('a', 32));
        Assert.True(options.Matches(new string('a', 32)));
        Assert.False(options.Matches(new string('b', 32)));
        Assert.False(new WebAccessOptions(null).Matches(""));
    }
}
