using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SonosMonitoring.Api;
using SonosMonitoring.Application.Collection;
using SonosMonitoring.Contracts.Collection;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.E2E.Tests.Collection;

/// <summary>Checks protected HTTP downloads and offline reading of detached, reduced diagnostic observations.</summary>
public sealed class CollectionExportTests
{
    private const string KEY = "export-test-key-012345678901234567890";

    /// <summary>A deployment key protects downloads even from loopback and cannot be bypassed by raw-export parameters.</summary>
    /// <param name="key">Missing or invalid request credential.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("incorrect")]
    public async Task UnauthorizedDownloadsAreDenied(string? key)
    {
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
        using var client = new HttpClient { BaseAddress = new Uri(api.Urls.Single()) };
        if (key is not null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", key);
        }
        using var response = await client.GetAsync("/api/collection/export?raw=true");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("schemaVersion", await response.Content.ReadAsStringAsync());
        await api.StopAsync();
    }

    /// <summary>A saved file retains failed-source evidence and topology references after live state changes and the API stops.</summary>
    [Fact]
    public async Task DownloadCanBeReadOfflineWithoutExposingRawIdentifiers()
    {
        await using var api = ApiHost.Build([], new Dictionary<string, string?>
        {
            ["CollectionSettings:Path"] = Path.Combine(Path.GetTempPath(), "sonos-isolated-" + Guid.NewGuid().ToString("N"), "settings.json"),
            ["Backend:Enabled"] = "false",
            ["Collector:Enabled"] = "false",
            ["Access:ApiKey"] = KEY
        });
        var state = api.Services.GetRequiredService<CollectorState>();
        DateTimeOffset observed = DateTimeOffset.UtcNow.AddHours(-1);
        state.DescriptionSucceeded(new PlayerDescription("RINCON_PRIVATE_FIRST", "Private room", "Private model"), "192.168.47.10", observed);
        state.DescriptionSucceeded(new PlayerDescription("RINCON_PRIVATE_SECOND", "Private other room", null), "192.168.47.11", observed);
        state.InterfaceSucceeded("RINCON_PRIVATE_FIRST", [new InterfaceCounters("private_interface", 42, 21, 3, 4, 5, 6, 7, 8)], observed);
        state.TopologySucceeded("RINCON_PRIVATE_FIRST", [new TopologyGroup("private_group", "RINCON_PRIVATE_FIRST",
            [new TopologyMember("RINCON_PRIVATE_SECOND", "Private other room", true, "RINCON_PRIVATE_FIRST")
            { ReportedAddress = "192.168.47.11" }])], observed);
        state.TopologyFailed("RINCON_PRIVATE_FIRST", observed.AddMinutes(1), "timeout");
        state.RangeResult("192.168.47.0/24", observed, null, 12, 2, "sweep_timeout");
        api.Urls.Clear();
        api.Urls.Add("http://127.0.0.1:0");
        await api.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(api.Urls.Single()) };
        client.DefaultRequestHeaders.Add("X-Api-Key", KEY);
        DateTimeOffset before = DateTimeOffset.UtcNow;
        using var response = await client.GetAsync("/api/collection/export?raw=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("sonosmonitoring-snapshot.json", response.Content.Headers.ContentDisposition?.ToString());
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        string json = System.Text.Encoding.UTF8.GetString(bytes);
        foreach (string value in new[] { "RINCON_PRIVATE", "192.168.47", "Private", "private_", KEY })
        {
            Assert.DoesNotContain(value, json);
        }

        state.DescriptionSucceeded(new PlayerDescription("RINCON_PRIVATE_FIRST", "Changed", null), "192.168.47.20", DateTimeOffset.UtcNow);
        state.TopologySucceeded("RINCON_PRIVATE_FIRST", [], DateTimeOffset.UtcNow);
        var live = (await client.GetFromJsonAsync<CollectorSnapshotDto>("/api/collection"))!;
        Assert.Equal("Changed", live.Devices[0].RoomName);
        await api.StopAsync();

        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            var document = JsonSerializer.Deserialize<CollectorSnapshotExportDto>(await File.ReadAllBytesAsync(path), JsonSerializerOptions.Web)!;
            Assert.Equal("sonosmonitoring.collection-snapshot", document.Format);
            Assert.Equal(1, document.SchemaVersion);
            Assert.Equal("reduced-identifiers-v1", document.Privacy);
            Assert.Equal(TimeSpan.Zero, document.CapturedAtUtc.Offset);
            Assert.InRange(document.CapturedAtUtc, before, DateTimeOffset.UtcNow);
            var first = document.Snapshot.Devices[0];
            Assert.Null(first.MetricLabel);
            var second = document.Snapshot.Devices[1];
            var group = Assert.Single(first.Groups);
            var member = Assert.Single(group.Members);
            Assert.Equal(first.DeviceId, group.CoordinatorDeviceId);
            Assert.Equal(first.DeviceId, member.ParentDeviceId);
            Assert.Equal(second.DeviceId, member.DeviceId);
            Assert.Equal(second.Address, member.ReportedAddress);
            Assert.Equal(second.RoomName, member.RoomName);
            Assert.NotEqual(first.DeviceId, second.DeviceId);
            Assert.True(first.TopologyStatus.IsStale);
            Assert.False(first.TopologyStatus.LastAttemptSucceeded);
            Assert.Equal("timeout", first.TopologyStatus.ErrorCode);
            Assert.Equal(observed, first.TopologyStatus.LastSuccessUtc);
            Assert.Equal(observed.AddMinutes(1), first.TopologyStatus.LastAttemptUtc);
            Assert.Equal(42UL, Assert.Single(first.Interfaces).ReceiveBytes);
            Assert.Null(first.ModelName);
            Assert.Equal("sweep_timeout", Assert.Single(document.Snapshot.Ranges).ErrorCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A never-started collector exports absence honestly without initiating discovery.</summary>
    [Fact]
    public async Task DisabledCollectorExportsAnEmptySnapshot()
    {
        await using var api = ApiHost.Build([], new Dictionary<string, string?>
        {
            ["CollectionSettings:Path"] = Path.Combine(Path.GetTempPath(), "sonos-isolated-" + Guid.NewGuid().ToString("N"), "settings.json"),
            ["Backend:Enabled"] = "false",
            ["Collector:Enabled"] = "false"
        });
        api.Urls.Clear();
        api.Urls.Add("http://127.0.0.1:0");
        await api.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(api.Urls.Single()) };
        var document = (await client.GetFromJsonAsync<CollectorSnapshotExportDto>("/api/collection/export"))!;
        Assert.False(document.Snapshot.Enabled);
        Assert.Null(document.Snapshot.HeartbeatUtc);
        Assert.Null(document.Snapshot.LastSweepUtc);
        Assert.Empty(document.Snapshot.Devices);
        Assert.Empty(document.Snapshot.Ranges);
        await api.StopAsync();
    }
}
