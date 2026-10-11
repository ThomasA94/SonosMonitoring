using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SonosMonitoring.Api;
using SonosMonitoring.Contracts.Collection;
using SonosMonitoring.Infrastructure.Collection.Configuration;
using SonosMonitoring.Infrastructure.History;

namespace SonosMonitoring.E2E.Tests.Collection;

/// <summary>Exercises configuration persistence, conflicts and runtime acquisition against isolated players.</summary>
[Collection("Loopback player ports")]
public sealed class CollectorConfigurationTests
{
    private const string KEY = "configuration-test-key-01234567890123456789";

    /// <summary>A disabled installation can start, reconfigure, stop and recover its settings through HTTP.</summary>
    [Fact]
    public async Task WebConfigurationControlsAcquisitionAndSurvivesRestart()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sonos-settings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        try
        {
            await using var players = new SimulatedPlayer();
            await players.StartAsync();
            Guid savedRevision;
            await using (var api = CreateApi(path))
            {
                await api.StartAsync();
                using var client = Client(api);
                using var denied = new HttpClient { BaseAddress = client.BaseAddress };
                Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync("/api/collection/configuration")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await denied.PostAsJsonAsync("/api/collection/configuration", new { })).StatusCode);
                var original = (await client.GetFromJsonAsync<CollectorConfigurationDto>("/api/collection/configuration"))!;
                Assert.False(original.Settings.Enabled);
                Assert.False((await client.GetFromJsonAsync<CollectorSnapshotDto>("/api/collection"))!.Enabled);

                original.Settings.Enabled = true;
                original.Settings.AllowedCidrs = ["127.0.0.2/32"];
                original.Settings.PollInterval = TimeSpan.FromSeconds(1);
                original.Settings.DiscoveryInterval = TimeSpan.FromSeconds(2);
                var identity = Guid.NewGuid();
                using var accepted = await SaveAsync(client, original, identity);
                Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
                var enabled = (await accepted.Content.ReadFromJsonAsync<CollectorConfigurationDto>())!;
                Assert.Equal(identity, enabled.Revision);
                var observed = await AwaitDeviceAsync(client, "127.0.0.2");
                Assert.Single(observed.Devices);

                using var duplicate = await SaveAsync(client, original, identity);
                Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
                Assert.Equal(observed.Devices[0].FirstSeenUtc, (await client.GetFromJsonAsync<CollectorSnapshotDto>("/api/collection"))!.Devices[0].FirstSeenUtc);
                using var conflict = await SaveAsync(client, original, Guid.NewGuid());
                Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
                original.Settings.PollInterval = TimeSpan.FromSeconds(2);
                using var reused = await SaveAsync(client, original, identity);
                Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);

                enabled.Settings.SeedAddresses = ["127.0.0.3"];
                using var invalid = await SaveAsync(client, enabled, Guid.NewGuid());
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
                Assert.Equal(identity, (await client.GetFromJsonAsync<CollectorConfigurationDto>("/api/collection/configuration"))!.Revision);
                enabled.Settings.SeedAddresses = [];
                enabled.Settings.AllowedCidrs = ["127.0.0.3/32"];
                using var moved = await SaveAsync(client, enabled, Guid.NewGuid());
                Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
                var changed = (await moved.Content.ReadFromJsonAsync<CollectorConfigurationDto>())!;
                var newSnapshot = await AwaitDeviceAsync(client, "127.0.0.3");
                Assert.DoesNotContain(newSnapshot.Devices, device => device.Address == "127.0.0.2");

                changed.Settings.Enabled = false;
                savedRevision = Guid.NewGuid();
                using var stopped = await SaveAsync(client, changed, savedRevision);
                Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
                var disabled = (await client.GetFromJsonAsync<CollectorSnapshotDto>("/api/collection"))!;
                Assert.False(disabled.Enabled);
                Assert.Null(disabled.HeartbeatUtc);
                await api.StopAsync();
            }

            await using var restarted = CreateApi(path);
            await restarted.StartAsync();
            using var restartedClient = Client(restarted);
            var persisted = (await restartedClient.GetFromJsonAsync<CollectorConfigurationDto>("/api/collection/configuration"))!;
            Assert.Equal(savedRevision, persisted.Revision);
            Assert.Equal(["127.0.0.3/32"], persisted.Settings.AllowedCidrs);
            Assert.False(persisted.Settings.Enabled);
            Assert.Equal(TimeSpan.FromSeconds(1), persisted.Settings.PollInterval);
            var competing = (await restartedClient.GetFromJsonAsync<CollectorConfigurationDto>("/api/collection/configuration"))!;
            persisted.Settings.MaxParallelism = 10;
            competing.Settings.MaxParallelism = 12;
            var responses = await Task.WhenAll(SaveAsync(restartedClient, persisted, Guid.NewGuid()), SaveAsync(restartedClient, competing, Guid.NewGuid()));
            try
            {
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            }
            finally
            {
                foreach (var response in responses)
                { response.Dispose(); }
            }
            await restarted.StopAsync();
        }
        finally
        {
            if (File.Exists(path))
            { File.Delete(path); }
            if (Directory.Exists(directory))
            { Directory.Delete(directory); }
        }
    }

    /// <summary>A failed durable write leaves the active generation and revision unchanged.</summary>
    [Fact]
    public async Task StorageFailureDoesNotActivateUnpersistedSettings()
    {
        string blocker = Path.GetTempFileName();
        try
        {
            await using var api = CreateApi(Path.Combine(blocker, "settings.json"));
            await api.StartAsync();
            using var client = Client(api);
            var original = (await client.GetFromJsonAsync<CollectorConfigurationDto>("/api/collection/configuration"))!;
            original.Settings.PollInterval = TimeSpan.FromSeconds(30);
            using var response = await SaveAsync(client, original, Guid.NewGuid());
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var unchanged = (await client.GetFromJsonAsync<CollectorConfigurationDto>("/api/collection/configuration"))!;
            Assert.Equal(Guid.Empty, unchanged.Revision);
            Assert.Equal(TimeSpan.FromSeconds(15), unchanged.Settings.PollInterval);
            await api.StopAsync();
        }
        finally { File.Delete(blocker); }
    }

    /// <summary>History capacity validation returns a client error and keeps the saved revision unchanged.</summary>
    [Fact]
    public async Task HistoryDeviceLimitRejectsHttpSaveBeforePersistence()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sonos-history-settings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        try
        {
            await using var api = CreateApi(path, services =>
                services.AddSingleton<ICollectorHistoryConfigurationGuard, ObservationHistoryConfigurationGuard>());
            await api.StartAsync();
            using var client = Client(api);
            CollectorConfigurationDto original = (await client.GetFromJsonAsync<CollectorConfigurationDto>(
                "/api/collection/configuration"))!;
            original.Settings.MaxKnownDevices = ObservationHistoryLimits.MAX_KNOWN_DEVICES + 1;

            using HttpResponseMessage response = await SaveAsync(client, original, Guid.NewGuid());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            CollectorConfigurationDto unchanged = (await client.GetFromJsonAsync<CollectorConfigurationDto>(
                "/api/collection/configuration"))!;
            Assert.Equal(original.Revision, unchanged.Revision);
            Assert.Equal(ObservationHistoryLimits.MAX_KNOWN_DEVICES, unchanged.Settings.MaxKnownDevices);
            Assert.False(File.Exists(path));
            await api.StopAsync();
        }
        finally
        {
            if (Directory.Exists(directory))
            { Directory.Delete(directory, recursive: true); }
        }
    }

    /// <summary>Builds an API with a dedicated settings file and loopback-only test targets.</summary>
    /// <param name="path">Isolated durable settings file.</param>
    /// <param name="configureServices">Optional test-only host services.</param>
    private static WebApplication CreateApi(string path, Action<IServiceCollection>? configureServices = null)
    {
        var app = ApiHost.Build([], new Dictionary<string, string?>
        {
            ["Environment"] = "Testing",
            ["Backend:Enabled"] = "false",
            ["Collector:Enabled"] = "false",
            ["Collector:AllowLoopback"] = "true",
            ["CollectionSettings:Path"] = path,
            ["Access:ApiKey"] = KEY
        }, configureServices);
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");
        return app;
    }

    /// <summary>Creates a credentialed client for the isolated listener.</summary>
    /// <param name="app">Started test API.</param>
    private static HttpClient Client(WebApplication app)
    {
        var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        client.DefaultRequestHeaders.Add("X-Api-Key", KEY);
        return client;
    }

    /// <summary>Submits one complete revision and its stable request identity.</summary>
    /// <param name="client">Isolated API transport.</param>
    /// <param name="document">Settings plus expected revision.</param>
    /// <param name="identity">Save identity.</param>
    private static async Task<HttpResponseMessage> SaveAsync(HttpClient client, CollectorConfigurationDto document, Guid identity)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/collection/configuration") { Content = JsonContent.Create(document) };
        request.Headers.Add("Idempotency-Key", identity.ToString());
        return await client.SendAsync(request);
    }

    /// <summary>Waits for a real collection pass against the simulator after activation.</summary>
    /// <param name="client">Isolated API transport.</param>
    /// <param name="address">Expected allowed player.</param>
    private static async Task<CollectorSnapshotDto> AwaitDeviceAsync(HttpClient client, string address)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var snapshot = (await client.GetFromJsonAsync<CollectorSnapshotDto>("/api/collection", timeout.Token))!;
            if (snapshot.Devices.Any(device => device.Address == address && device.Interfaces.Count > 0))
            { return snapshot; }
            await Task.Delay(100, timeout.Token);
        }
    }
}
