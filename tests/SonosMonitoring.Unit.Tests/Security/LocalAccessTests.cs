using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SonosMonitoring.Api.Security;

namespace SonosMonitoring.Unit.Tests.Security;

/// <summary>Checks the initial API trust boundary, including spoofed forwarding headers.</summary>
public sealed class LocalAccessTests
{
    /// <summary>Only actual loopback callers receive unauthenticated local access.</summary>
    /// <param name="address">Socket peer address.</param>
    /// <param name="expected">Whether the next middleware should run.</param>
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("192.168.6.20", false)]
    public async Task SocketPeerControlsLocalAccess(string address, bool expected)
    {
        var reached = false;
        var middleware = new LocalAccessMiddleware(_ => { reached = true; return Task.CompletedTask; }, new ConfigurationBuilder().Build());
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        context.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
        await middleware.InvokeAsync(context);
        Assert.Equal(expected, reached);
    }

    /// <summary>An explicitly configured key is required even for a local caller.</summary>
    /// <param name="provided">Whether the correct header is present.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeploymentKeyProtectsAllCallers(bool provided)
    {
        const string KEY = "test-only-key-012345678901234567890123456789";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Access:ApiKey"] = KEY }).Build();
        var reached = false;
        var middleware = new LocalAccessMiddleware(_ => { reached = true; return Task.CompletedTask; }, config);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["X-Api-Key"] = provided ? KEY : "incorrect";
        await middleware.InvokeAsync(context);
        Assert.Equal(provided, reached);
    }
}
