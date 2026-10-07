using Devbone.Frameworks.Ddd.Repositories;

namespace SonosMonitoring.Application.Monitoring;

/// <summary>Reads an existing commit token at a projected position without inventing a concurrency token.</summary>
public sealed class MonitoringTokenReader
{
    private readonly IEventStreamReader _reader;

    /// <summary>Receives the existing framework persistence read port.</summary>
    /// <param name="reader">Reader for retained monitoring policy events.</param>
    public MonitoringTokenReader(IEventStreamReader reader) => this._reader = reader;

    /// <summary>Returns only the token matching the state the user actually reviewed.</summary>
    /// <param name="deviceKey">Canonical device identity.</param>
    /// <param name="eventSequence">Displayed projection position.</param>
    public string? Read(string deviceKey, long eventSequence) =>
        this._reader.LoadAggregate(MonitoringIdentity.ForDevice(deviceKey))
            .SingleOrDefault(stored => stored.EventSequence == eventSequence)?.AggregateConcurrencyToken?.ToString();
}
