using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SonosMonitoring.Api;
using SonosMonitoring.Contracts.Diagnostics;
using SonosMonitoring.Observability.Diagnostics;

namespace SonosMonitoring.E2E.Tests.Diagnostics;

/// <summary>Exercises protected HTTP log reads against real isolated disk storage and a restarted API.</summary>
public sealed class ApplicationLogApiTests
{
    /// <summary>Logs survive restart, reject invalid queries, and never disclose a configured credential.</summary>
    [Fact]
    public async Task Logs_are_protected_filtered_and_retained_after_restart()
    {
        const string KEY = "logs-api-isolated-secret-0123456789012345";
        string directory = Path.Combine(Path.GetTempPath(), "sonos-log-api-" + Guid.NewGuid().ToString("N"));
        var configuration = new Dictionary<string, string?>
        {
            ["Environment"] = "Testing",
            ["CollectionSettings:Path"] = Path.Combine(directory, "settings.json"),
            ["ApplicationLogs:Directory"] = Path.Combine(directory, "logs"),
            ["Backend:Enabled"] = "false",
            ["Collector:Enabled"] = "false",
            ["Access:ApiKey"] = KEY,
        };
        try
        {
            await using (var api = ApiHost.Build([], configuration))
            {
                api.Urls.Clear();
                api.Urls.Add("http://127.0.0.1:0");
                await api.StartAsync();
                api.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SonosMonitoring.NetworkTest")
                    .LogWarning("Connection to player timed out; credential={Credential}", KEY);
                var stored = api.Services.GetRequiredService<ApplicationLogStore>().Read("api");
                Assert.True(stored.Entries.Any(entry => entry.Level == "Warning" && entry.Message.Contains("player", StringComparison.Ordinal)),
                    $"Expected warning immediately after logging. Sources: {string.Join(',', stored.Sources.Select(source => $"{source.Source}:{source.State}:{source.SkippedLines}"))}; "
                    + $"entries={stored.Entries.Count}; warnings={stored.Entries.Count(entry => entry.Level == "Warning")}; "
                    + $"sanitizationTimeouts={stored.Entries.Count(entry => entry.Message.Contains("sanitization timed out", StringComparison.Ordinal))}; "
                    + $"providers={api.Services.GetServices<ILoggerProvider>().Count(provider => provider is ApplicationLogProvider)}.");
                using var client = new HttpClient { BaseAddress = new Uri(api.Urls.Single()) };
                using var denied = await client.GetAsync("/api/diagnostics/logs");
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                client.DefaultRequestHeaders.Add("X-Api-Key", KEY);
                var logs = await client.GetFromJsonAsync<ApplicationLogPage>("/api/diagnostics/logs?source=api&minimumLevel=Warning&search=player&limit=1");
                Assert.NotNull(logs);
                Assert.Single(logs.Entries);
                Assert.Equal("Warning", logs.Entries[0].Level);
                Assert.DoesNotContain(KEY, logs.Entries[0].Message);
                foreach (var query in new[] { "source=../web", "minimumLevel=Trace", "limit=501", "limit=0", "search=" + new string('x', 201) })
                {
                    using var invalid = await client.GetAsync("/api/diagnostics/logs?" + query);
                    Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
                }
                await api.StopAsync();
            }

            await using (var restarted = ApiHost.Build([], configuration))
            {
                restarted.Urls.Clear();
                restarted.Urls.Add("http://127.0.0.1:0");
                await restarted.StartAsync();
                using var client = new HttpClient { BaseAddress = new Uri(restarted.Urls.Single()) };
                client.DefaultRequestHeaders.Add("X-Api-Key", KEY);
                var retained = await client.GetFromJsonAsync<ApplicationLogPage>("/api/diagnostics/logs?source=&search=player");
                Assert.Single(retained!.Entries);
                Assert.Contains(retained.Sources, source => source.Source == "api" && source.State == "available");
                Assert.Contains(retained.Sources, source => source.Source == "projections" && source.State == "missing");
                await restarted.StopAsync();
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
