using Microsoft.Extensions.Logging;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Captures formatted collector diagnostics for behavioral assertions.</summary>
internal sealed class NetworkDiagnosticTestLogger : ILogger
{
    /// <summary>Gets the recorded levels and messages in emission order.</summary>
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    /// <summary>Accepts scopes without introducing additional test state.</summary>
    /// <typeparam name="TState">Scope state type.</typeparam>
    /// <param name="state">Unused scope state.</param>
    /// <returns>No scope resource.</returns>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <summary>Captures all diagnostic levels.</summary>
    /// <param name="logLevel">Requested level.</param>
    /// <returns>Always true.</returns>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <summary>Records the same text emitted to an application log sink.</summary>
    /// <typeparam name="TState">Structured event state.</typeparam>
    /// <param name="logLevel">Event severity.</param>
    /// <param name="eventId">Unused event identifier.</param>
    /// <param name="state">Structured diagnostic values.</param>
    /// <param name="exception">Optional exception.</param>
    /// <param name="formatter">Standard logging formatter.</param>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        this.Entries.Add((logLevel, formatter(state, exception)));
    }
}
