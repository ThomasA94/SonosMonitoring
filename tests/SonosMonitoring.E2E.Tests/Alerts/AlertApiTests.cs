using System.Net;
using SonosMonitoring.Api;

namespace SonosMonitoring.E2E.Tests.Alerts;

/// <summary>Checks alert endpoint binding and failure visibility behind the global access boundary.</summary>
public sealed class AlertApiTests
{
    /// <summary>Unconfigured rules never appear as an empty healthy alert list, and access remains protected.</summary>
    [Fact]
    public async Task MissingConfigurationAndInvalidAccessAreExplicit()
    {
        const string KEY = "alert-api-test-key-01234567890123456789";
        await using var api = ApiHost.Build([], new Dictionary<string, string?>
        {
            ["CollectionSettings:Path"] = Path.Combine(Path.GetTempPath(), "sonos-isolated-" + Guid.NewGuid().ToString("N"), "settings.json"),
            ["Backend:Enabled"] = "false",
            ["Collector:Enabled"] = "false",
            ["Access:ApiKey"] = KEY
        });
        api.Urls.Clear();
        api.Urls.Add("http://127.0.0.1:0");
        await api.StartAsync();
        using var client = new HttpClient { BaseAddress = new(api.Urls.Single()) };
        using var denied = await client.GetAsync("/api/alerts");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        client.DefaultRequestHeaders.Add("X-Api-Key", KEY);
        using var unconfigured = await client.GetAsync("/api/alerts");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unconfigured.StatusCode);
        Assert.Contains("alerts.unconfigured", await unconfigured.Content.ReadAsStringAsync());
        using var history = await client.GetAsync("/api/collection/history");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, history.StatusCode);
        Assert.Contains("history.unconfigured", await history.Content.ReadAsStringAsync());
        await api.StopAsync();
    }
}
