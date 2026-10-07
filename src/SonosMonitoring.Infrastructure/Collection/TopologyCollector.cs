using System.Collections.Concurrent;

namespace SonosMonitoring.Infrastructure.Collection;

/// <summary>Polls topology with independent failure backoff for already admitted player identities.</summary>
public sealed class TopologyCollector
{
    private readonly PlayerProbe _probe;
    private readonly CollectorState _state;
    private readonly CollectorOptions _options;
    private readonly ConcurrentDictionary<string, (DateTimeOffset Next, int Failures)> _schedule = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _active = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _invalidated = new(StringComparer.Ordinal);

    /// <summary>Coalesces an event hint into one reconciliation, without trusting its payload as current state.</summary>
    /// <param name="id">Already admitted player identity.</param>
    public void Invalidate(string id) => this._invalidated[id] = 0;

    /// <summary>Creates the source-specific polling coordinator.</summary>
    /// <param name="probe">Bounded read-only transport.</param>
    /// <param name="state">Bounded observation cache.</param>
    /// <param name="options">Polling and structural limits.</param>
    public TopologyCollector(PlayerProbe probe, CollectorState state, CollectorOptions options)
    {
        this._probe = probe;
        this._state = state;
        this._options = options;
    }

    /// <summary>Attempts one topology read without preventing later counter collection on source failure.</summary>
    /// <param name="id">Identity just validated and admitted to the bounded cache.</param>
    /// <param name="address">The address validated for this identity.</param>
    /// <param name="cancellationToken">Shared sweep deadline and host shutdown.</param>
    /// <returns>A task completing after observation or a safely recorded source failure.</returns>
    public async Task CollectAsync(string id, string address, CancellationToken cancellationToken)
    {
        if (this._active.TryAdd(id, 0) == false)
        {
            return;
        }

        try
        {
            await this.CollectSourceAsync(id, address, cancellationToken);
        }
        finally
        {
            this._active.TryRemove(id, out _);
        }
    }

    /// <summary>Applies the source schedule while holding the per-identity collection slot.</summary>
    /// <param name="id">Admitted player identity.</param>
    /// <param name="address">Validated literal target.</param>
    /// <param name="cancellationToken">Sweep cancellation.</param>
    /// <returns>A task for this source attempt.</returns>
    private async Task CollectSourceAsync(string id, string address, CancellationToken cancellationToken)
    {
        this._schedule.TryGetValue(id, out (DateTimeOffset Next, int Failures) schedule);
        if (this._invalidated.TryRemove(id, out _) == false && DateTimeOffset.UtcNow < schedule.Next)
        {
            return;
        }

        try
        {
            string xml = await this._probe.GetTopologyAsync(address, cancellationToken);
            var groups = ZoneGroupTopologyParser.Parse(xml, this._options.MaxKnownDevices);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            this._state.TopologySucceeded(id, groups, now);
            this._schedule[id] = (now + this._options.PollInterval, 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or InvalidDataException or FormatException)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string code = exception switch
            {
                OperationCanceledException => "timeout",
                HttpRequestException => "http_failure",
                InvalidDataException => "response_too_large",
                IOException => "http_failure",
                _ => "invalid_response"
            };
            this._state.TopologyFailed(id, now, code);
            int failures = Math.Min(schedule.Failures + 1, 5);
            double delay = Math.Min(300, this._options.PollInterval.TotalSeconds * Math.Pow(2, failures));
            this._schedule[id] = (now + TimeSpan.FromSeconds(delay + Random.Shared.NextDouble()), failures);
        }
    }
}
