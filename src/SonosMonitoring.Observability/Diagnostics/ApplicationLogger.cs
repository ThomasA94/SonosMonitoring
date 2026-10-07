using Microsoft.Extensions.Logging;

namespace SonosMonitoring.Observability.Diagnostics;

/// <summary>Filters framework noise and omits scopes and exception stack traces from diagnostic storage.</summary>
internal sealed class ApplicationLogger : ILogger
{
    private readonly ApplicationLogStore _store;
    private readonly string _source;
    private readonly string _category;

    /// <summary>Creates a logger bound to a single host and category.</summary>
    /// <param name="store">Sanitizing persistent log store.</param>
    /// <param name="source">Validated host name.</param>
    /// <param name="category">Logger category.</param>
    public ApplicationLogger(ApplicationLogStore store, string source, string category)
    {
        this._store = store;
        this._source = source;
        this._category = category;
    }

    /// <summary>Ignores scopes because their arbitrary state may include request credentials.</summary>
    /// <param name="state">Ignored scope state.</param>
    /// <typeparam name="TState">Scope state type.</typeparam>
    /// <returns>No persistent scope.</returns>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <summary>Keeps application information and warnings or higher from all categories.</summary>
    /// <param name="logLevel">Candidate severity.</param>
    /// <returns>Whether this event belongs in the bounded history.</returns>
    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= LogLevel.Information && logLevel <= LogLevel.Critical
            && (logLevel >= LogLevel.Warning || this._category == "Microsoft.Hosting.Lifetime" || (this._category.StartsWith("Microsoft.", StringComparison.Ordinal) == false
                && this._category.StartsWith("System.", StringComparison.Ordinal) == false));
    }

    /// <summary>Persists sanitized formatted text and the exception type and message, without stack traces.</summary>
    /// <param name="logLevel">Event severity.</param>
    /// <param name="eventId">Event identifier.</param>
    /// <param name="state">Event state formatted by the standard logging pipeline.</param>
    /// <param name="exception">Optional failure; stack traces and nested exceptions are not persisted.</param>
    /// <param name="formatter">Formatter for the event message.</param>
    /// <typeparam name="TState">Event state type.</typeparam>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (this.IsEnabled(logLevel) == false)
        {
            return;
        }

        // Passing null avoids custom formatters appending an unbounded exception stack trace.
        var message = formatter(state, null);
        if (exception is not null)
        {
            message = $"{message} [{exception.GetType().Name}: {exception.Message}]";
        }

        this._store.Append(this._source, logLevel, this._category, eventId.Id, message);
    }
}
