using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SonosMonitoring.Observability.Diagnostics;

namespace SonosMonitoring.Unit.Tests.Diagnostics;

/// <summary>Verifies bounded diagnostic persistence, filtering, sanitization, and storage failure isolation.</summary>
public sealed class ApplicationLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sonos-log-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Independent store instances recover flushed events after a host restart.</summary>
    [Fact]
    public void RestartRecoversEventsAcrossSourcesAndReportsMissingSources()
    {
        var first = this.CreateStore();
        Write(first, "api", "Collector", LogLevel.Error, "DNS lookup failed for rabbitmq");
        Write(first, "web", "Connection", LogLevel.Warning, "API connection failed");

        var recovered = this.CreateStore().Read();
        Assert.Equal(2, recovered.Entries.Count);
        Assert.Equal("web", recovered.Entries[0].Source);
        Assert.Equal("api", recovered.Entries[1].Source);
        Assert.All(recovered.Entries, entry => Assert.Equal(TimeSpan.Zero, entry.TimestampUtc.Offset));
        Assert.Equal("missing", recovered.Sources.Single(source => source.Source == "projections").State);
        Assert.Equal("available", recovered.Sources.Single(source => source.Source == "api").State);
    }

    /// <summary>Severity, source, category, search and result bounds compose without returning older matches first.</summary>
    [Fact]
    public void FiltersResultsAndSuppressesFrameworkNoise()
    {
        var store = this.CreateStore();
        Write(store, "api", "Collector", LogLevel.Debug, "debug noise");
        Write(store, "api", "Microsoft.AspNetCore.Hosting", LogLevel.Information, "request noise");
        Write(store, "api", "System.Net.Http.HttpClient", LogLevel.Information, "HTTP noise");
        Write(store, "web", "Connection", LogLevel.Error, "Network other source");
        Write(store, "api", "Collector", LogLevel.Information, "Network information");
        Write(store, "api", "Collector", LogLevel.Warning, "Network older");
        Write(store, "api", "Microsoft.Hosting.Lifetime", LogLevel.Error, "NETWORK latest");

        Assert.Equal(4, store.Read().Entries.Count);
        var page = store.Read("api", "warning", "network", 1);
        Assert.True(page.Truncated);
        Assert.Equal("NETWORK latest", Assert.Single(page.Entries).Message);
        Assert.Equal("api", Assert.Single(page.Sources).Source);
        Assert.Equal(1, page.Limit);
        Assert.Equal(2, store.Read("api", "Information", "Collector").Entries.Count);
    }

    /// <summary>Rotations retain at most three one-megabyte files while preserving the latest complete record.</summary>
    [Fact]
    public void RotatesAndBoundsRetainedBytesAndMessages()
    {
        var store = this.CreateStore();
        for (var index = 0; index < 1000; index++)
        {
            Write(store, "api", "Collector", LogLevel.Warning, $"event-{index:D4} " + new string('x', 6000));
        }

        var files = Directory.GetFiles(this._directory);
        Assert.Equal(3, files.Length);
        Assert.All(files, path => Assert.InRange(new FileInfo(path).Length, 1, 1048576));
        var page = this.CreateStore().Read(limit: 500);
        Assert.True(page.Truncated);
        Assert.Equal(500, page.Entries.Count);
        Assert.StartsWith("event-0999 ", page.Entries[0].Message);
        Assert.All(page.Entries, entry => Assert.InRange(entry.Message.Length, 1, 4000));
        Assert.Empty(store.Read(search: "event-0000").Entries);
    }

    /// <summary>Configured secrets and credential-shaped fields are redacted both on disk and when queried.</summary>
    [Fact]
    public void RedactsSecretsBeforePersistenceAndOmitsExceptionStack()
    {
        var store = this.CreateStore(new Dictionary<string, string?>
        {
            ["Access:ApiKey"] = "configured-api-key-123",
            ["Database:ConnectionString"] = "Host=db;Username=user;Password=configured-password"
        });
        using var provider = new ApplicationLogProvider(store, "api");
        var logger = provider.CreateLogger("Collector");
        logger.LogError(new InvalidOperationException("password=exception-secret"),
            "{Message}", "configured-api-key-123 Host=db;Username=user;Password=configured-password "
            + "password=inline-secret token='quoted secret' Authorization: Bearer bearer-secret\n"
            + "https://user:uri-secret@host/path?value=query-secret\n"
            + "{\"password\":\"json-secret\"}\nCookie: session=cookie-secret; second=second-cookie-secret\n"
            + "   at Private.Internal.StackFrame() in /private/path.cs:line 42\n");

        var entry = Assert.Single(store.Read().Entries);
        Assert.Contains("InvalidOperationException", entry.Message);
        Assert.Contains("[REDACTED]", entry.Message);
        var persisted = File.ReadAllText(Assert.Single(Directory.GetFiles(this._directory)));
        foreach (var secret in new[] { "configured-api-key-123", "configured-password", "inline-secret", "quoted secret", "bearer-secret", "uri-secret", "query-secret", "json-secret", "exception-secret", "cookie-secret", "second-cookie-secret", "Private.Internal.StackFrame", "/private/path.cs" })
        {
            Assert.DoesNotContain(secret, persisted);
            Assert.DoesNotContain(secret, entry.Message);
        }

        Assert.DoesNotContain(nameof(RedactsSecretsBeforePersistenceAndOmitsExceptionStack), persisted);
    }

    /// <summary>Malformed, oversized, and incomplete records cannot prevent other retained events from being read.</summary>
    [Fact]
    public void SkipsCorruptAndIncompleteLinesWithoutLosingValidEvents()
    {
        var store = this.CreateStore();
        Write(store, "api", "Collector", LogLevel.Error, "valid diagnostic");
        File.AppendAllText(Path.Combine(this._directory, "api.0.jsonl"), "not json\n" + new string('x', 40000) + "\n{\"TimestampUtc\":");

        var page = store.Read("api");
        Assert.Equal("valid diagnostic", Assert.Single(page.Entries).Message);
        Assert.Equal(3, Assert.Single(page.Sources).SkippedLines);
    }

    /// <summary>A broken log directory never propagates an IO failure into application logging.</summary>
    [Fact]
    public void ReportsFailedStorageAndRecoversAfterRepair()
    {
        File.WriteAllText(this._directory, "blocking file");
        var store = this.CreateStore();
        Write(store, "api", "Collector", LogLevel.Error, "cannot persist yet");
        Assert.Equal("unavailable", Assert.Single(store.Read("api").Sources).State);

        File.Delete(this._directory);
        Write(store, "api", "Collector", LogLevel.Error, "storage recovered");
        Assert.Equal("available", Assert.Single(store.Read("api").Sources).State);
        Assert.Equal("storage recovered", Assert.Single(store.Read("api").Entries).Message);
    }

    /// <summary>Disabling the provider leaves storage untouched and explains the empty result.</summary>
    [Fact]
    public void DisabledStorageDoesNotCreateFiles()
    {
        var store = this.CreateStore(new Dictionary<string, string?> { ["ApplicationLogs:Enabled"] = "false" });
        Write(store, "api", "Collector", LogLevel.Error, "ignored event");
        Assert.False(Directory.Exists(this._directory));
        Assert.All(store.Read().Sources, source => Assert.Equal("disabled", source.State));
    }

    /// <summary>Rejects paths, unsupported levels, excessive search and result counts before reading files.</summary>
    [Fact]
    public void RejectsUnsupportedQueryInputs()
    {
        var store = this.CreateStore();
        Assert.Throws<ArgumentException>(() => store.Read("../credentials"));
        Assert.Throws<ArgumentException>(() => store.Read(minimumLevel: "Debug"));
        Assert.Throws<ArgumentException>(() => store.Read(minimumLevel: "invalid"));
        Assert.Throws<ArgumentException>(() => store.Read(limit: 501));
        Assert.Throws<ArgumentException>(() => store.Read(limit: 0));
        Assert.Throws<ArgumentException>(() => store.Read(search: new string('x', 201)));
        Assert.Throws<ArgumentException>(() => new ApplicationLogProvider(store, "../other"));
    }

    /// <summary>Removes only this test's isolated temporary storage.</summary>
    public void Dispose()
    {
        if (Directory.Exists(this._directory))
        {
            Directory.Delete(this._directory, true);
        }
        else if (File.Exists(this._directory))
        {
            File.Delete(this._directory);
        }
    }

    /// <summary>Creates independent store instances against the same isolated test directory.</summary>
    /// <param name="settings">Additional settings or known secrets.</param>
    /// <returns>A store that is independent of shared application infrastructure.</returns>
    private ApplicationLogStore CreateStore(Dictionary<string, string?>? settings = null)
    {
        settings ??= new Dictionary<string, string?>();
        settings["ApplicationLogs:Directory"] = this._directory;
        return new ApplicationLogStore(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    /// <summary>Exercises the same provider and formatter path used by host loggers.</summary>
    /// <param name="store">Target log store.</param>
    /// <param name="source">Application host.</param>
    /// <param name="category">Logger category.</param>
    /// <param name="level">Log severity.</param>
    /// <param name="message">Formatted message.</param>
    private static void Write(ApplicationLogStore store, string source, string category, LogLevel level, string message)
    {
        using var provider = new ApplicationLogProvider(store, source);
        provider.CreateLogger(category).Log(level, new EventId(42), message, null, (state, _) => state);
    }
}
