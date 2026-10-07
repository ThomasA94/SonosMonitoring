using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SonosMonitoring.Contracts.Diagnostics;

namespace SonosMonitoring.Observability.Diagnostics;

/// <summary>Persists a small rolling diagnostic history independent of databases and message brokers.</summary>
public sealed class ApplicationLogStore
{
    private const int MAX_FILE_BYTES = 1048576;
    private const int FILE_COUNT = 3;
    private const int MAX_LINE_CHARACTERS = 32768;
    private static readonly string[] _sources = ["api", "web", "projections"];
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly bool _enabled;
    private readonly ApplicationLogRedactor _redactor;
    private readonly HashSet<string> _failedWriters = new(StringComparer.Ordinal);

    /// <summary>Uses an explicitly shared directory or the current working directory's local log folder.</summary>
    /// <param name="configuration">ApplicationLogs:Enabled and ApplicationLogs:Directory settings.</param>
    public ApplicationLogStore(IConfiguration configuration)
    {
        this._enabled = configuration.GetValue("ApplicationLogs:Enabled", true);
        this._directory = configuration["ApplicationLogs:Directory"] ?? Path.Combine(Directory.GetCurrentDirectory(), ".local", "logs");
        this._redactor = new ApplicationLogRedactor(configuration);
    }

    /// <summary>Returns bounded retained logs; unreadable sources remain explicit in the response.</summary>
    /// <param name="source">Optional exact allowlisted source name.</param>
    /// <param name="minimumLevel">Information, Warning, Error, or Critical; defaults to Information.</param>
    /// <param name="search">Optional case-insensitive message or category substring, at most 200 characters.</param>
    /// <param name="limit">Maximum result count between 1 and 500.</param>
    /// <returns>Newest matching events and per-source storage status.</returns>
    /// <exception cref="ArgumentException">A filter falls outside the supported bounds.</exception>
    public ApplicationLogPage Read(string? source = null, string? minimumLevel = null, string? search = null, int limit = 200)
    {
        if (source is not null)
        {
            ValidateSource(source);
        }

        if (Enum.TryParse<LogLevel>(minimumLevel ?? "Information", true, out var severity) == false
            || severity < LogLevel.Information || severity > LogLevel.Critical)
        {
            throw new ArgumentException("Unsupported minimum log level.", nameof(minimumLevel));
        }

        if (limit is < 1 or > 500 || search?.Length > 200)
        {
            throw new ArgumentException("Log query exceeds supported bounds.");
        }

        var entries = new List<ApplicationLogEntry>(limit + 1);
        var statuses = new List<ApplicationLogSourceStatus>();
        var truncated = false;
        lock (this._gate)
        {
            foreach (var selected in source is null ? _sources : [source])
            {
                if (this._enabled == false)
                {
                    statuses.Add(new ApplicationLogSourceStatus(selected, "disabled", 0));
                    continue;
                }

                var found = false;
                var unavailable = this._failedWriters.Contains(selected);
                var skipped = 0;
                for (var index = 0; index < FILE_COUNT; index++)
                {
                    try
                    {
                        using var stream = new FileStream(this.GetPath(selected, index), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        found = true;
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        foreach (var line in ReadBoundedLines(reader))
                        {
                            var entry = this.Parse(line, selected);
                            if (entry is null)
                            {
                                skipped++;
                                continue;
                            }

                            if (Enum.Parse<LogLevel>(entry.Level) < severity
                                || (string.IsNullOrEmpty(search) == false
                                    && entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase) == false
                                    && entry.Category.Contains(search, StringComparison.OrdinalIgnoreCase) == false))
                            {
                                continue;
                            }

                            entries.Add(entry);
                            if (entries.Count > limit)
                            {
                                entries.Sort((left, right) => right.TimestampUtc.CompareTo(left.TimestampUtc));
                                entries.RemoveAt(entries.Count - 1);
                                truncated = true;
                            }
                        }
                    }
                    catch (FileNotFoundException)
                    {
                        // Rotation may remove a file between consecutive reads; the next refresh converges.
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // A host that has not emitted any retained events has no files yet.
                    }
                    catch (Exception exception) when (IsStorageException(exception))
                    {
                        unavailable = true;
                    }
                }

                statuses.Add(new ApplicationLogSourceStatus(selected, unavailable ? "unavailable" : found ? "available" : "missing", skipped));
            }
        }

        return new ApplicationLogPage(entries.OrderByDescending(entry => entry.TimestampUtc).ToArray(), statuses, truncated, limit);
    }

    /// <summary>Writes sanitized events atomically within this host and bounds its retained disk space.</summary>
    /// <param name="source">Previously validated application host name.</param>
    /// <param name="level">Severity of the event.</param>
    /// <param name="category">Logger category.</param>
    /// <param name="eventId">Numeric event identifier.</param>
    /// <param name="message">Formatted message, without an exception stack trace.</param>
    internal void Append(string source, LogLevel level, string category, int eventId, string message)
    {
        if (this._enabled == false)
        {
            return;
        }

        var entry = new ApplicationLogEntry(DateTimeOffset.UtcNow, source, level.ToString(), this._redactor.Sanitize(category, 300),
            eventId, this._redactor.Sanitize(message, 4000));
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n");
        lock (this._gate)
        {
            try
            {
                Directory.CreateDirectory(this._directory);
                var path = this.GetPath(source, 0);
                if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > MAX_FILE_BYTES)
                {
                    File.Delete(this.GetPath(source, FILE_COUNT - 1));
                    for (var index = FILE_COUNT - 2; index >= 0; index--)
                    {
                        var previous = this.GetPath(source, index);
                        if (File.Exists(previous))
                        {
                            File.Move(previous, this.GetPath(source, index + 1), true);
                        }
                    }
                }

                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete);
                stream.Write(bytes);
                this._failedWriters.Remove(source);
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                // Diagnostics must never take down the application or recursively log their own failure.
                this._failedWriters.Add(source);
            }
        }
    }

    /// <summary>Rejects source names before using them in filesystem paths.</summary>
    /// <param name="source">Requested host name.</param>
    internal static void ValidateSource(string source)
    {
        if (_sources.Contains(source, StringComparer.Ordinal) == false)
        {
            throw new ArgumentException("Unknown application log source.", nameof(source));
        }
    }

    /// <summary>Builds a path only from validated sources and fixed generation indices.</summary>
    /// <param name="source">Validated host name.</param>
    /// <param name="index">Zero for the current file, otherwise its rotation generation.</param>
    /// <returns>Log file path.</returns>
    private string GetPath(string source, int index) => Path.Combine(this._directory, $"{source}.{index}.jsonl");

    /// <summary>Validates and sanitizes retained records again before returning them to a client.</summary>
    /// <param name="line">Bounded JSON line, or null for an oversized line.</param>
    /// <param name="source">Expected file owner.</param>
    /// <returns>A valid safe event, or null for corrupt and incomplete records.</returns>
    private ApplicationLogEntry? Parse(string? line, string source)
    {
        if (line is null)
        {
            return null;
        }

        try
        {
            var entry = JsonSerializer.Deserialize<ApplicationLogEntry>(line);
            if (entry is null || entry.Source != source || entry.Message is null || entry.Category is null
                || entry.TimestampUtc == default || entry.TimestampUtc.Offset != TimeSpan.Zero
                || Enum.TryParse<LogLevel>(entry.Level, out var level) == false
                || level < LogLevel.Information || level > LogLevel.Critical)
            {
                return null;
            }

            return entry with { Level = level.ToString(), Message = this._redactor.Sanitize(entry.Message, 4000), Category = this._redactor.Sanitize(entry.Category, 300) };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Consumes malformed oversized lines without allocating an unbounded string.</summary>
    /// <param name="reader">Reader for one retained file.</param>
    /// <returns>Complete bounded lines; null identifies a discarded oversized or incomplete line.</returns>
    private static IEnumerable<string?> ReadBoundedLines(StreamReader reader)
    {
        var builder = new StringBuilder();
        var oversized = false;
        var consumed = 0;
        while (reader.Read() is var character && character >= 0 && consumed++ < MAX_FILE_BYTES * 2)
        {
            if (character == '\n')
            {
                yield return oversized ? null : builder.ToString();
                builder.Clear();
                oversized = false;
            }
            else if (builder.Length < MAX_LINE_CHARACTERS)
            {
                builder.Append((char)character);
            }
            else
            {
                oversized = true;
            }
        }

        if (builder.Length > 0 || oversized)
        {
            yield return null;
        }
    }

    /// <summary>Recognizes environmental filesystem failures that diagnostics must tolerate.</summary>
    /// <param name="exception">Failure thrown by a filesystem operation.</param>
    /// <returns>Whether storage is unavailable instead of the application being faulty.</returns>
    private static bool IsStorageException(Exception exception) => exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException;
}
