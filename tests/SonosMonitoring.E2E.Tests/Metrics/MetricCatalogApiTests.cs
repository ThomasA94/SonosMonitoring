using System.Net;
using System.Text.Json;
using SonosMonitoring.Api;

namespace SonosMonitoring.E2E.Tests.Metrics;

/// <summary>Checks that metric selection metadata is returned through the protected API.</summary>
public sealed class MetricCatalogApiTests
{
    /// <summary>The catalog is protected and returns selector units and label scopes.</summary>
    [Fact]
    public async Task CatalogRequiresAccessAndListsSupportedSelectors()
    {
        const string KEY = "metric-catalog-api-test-key-0123456789";
        await using var api = ApiHost.Build([], new Dictionary<string, string?>
        {
            ["CollectionSettings:Path"] = Path.Combine(Path.GetTempPath(), "sonos-isolated-" + Guid.NewGuid().ToString("N"), "settings.json"),
            ["Backend:Enabled"] = "false",
            ["Collector:Enabled"] = "false",
            ["Access:ApiKey"] = KEY,
        });
        api.Urls.Clear();
        api.Urls.Add("http://127.0.0.1:0");
        await api.StartAsync();
        using var client = new HttpClient { BaseAddress = new(api.Urls.Single()) };

        using var denied = await client.GetAsync("/api/metrics/history/catalog");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        client.DefaultRequestHeaders.Add("X-Api-Key", KEY);
        using var response = await client.GetAsync("/api/metrics/history/catalog");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement metrics = document.RootElement.GetProperty("metrics");
        Assert.Contains(metrics.EnumerateArray(), item => item.GetProperty("selector").GetString() == "receive_packets_rate" &&
            item.GetProperty("unit").GetString() == "packets/second" && item.GetProperty("scope").GetString() == "interface" &&
            item.GetProperty("labelKey").GetString() == "interface");
        Assert.Contains(metrics.EnumerateArray(), item => item.GetProperty("selector").GetString() == "source_age" &&
            item.GetProperty("unit").GetString() == "seconds" && item.GetProperty("scope").GetString() == "source");
        Assert.Contains(metrics.EnumerateArray(), item => item.GetProperty("selector").GetString() == "gena_subscribed" &&
            item.GetProperty("unit").GetString() == "boolean" && item.GetProperty("scope").GetString() == "device");
        await api.StopAsync();
    }
}
