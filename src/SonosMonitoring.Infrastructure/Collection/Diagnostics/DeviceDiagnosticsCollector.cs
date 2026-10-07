using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;

namespace SonosMonitoring.Infrastructure.Collection.Diagnostics;

/// <summary>Schedules independent bounded read-only diagnostic sources with per-source retry and backoff.</summary>
public sealed class DeviceDiagnosticsCollector
{
    private readonly PlayerProbe _probe;
    private readonly CollectorState _state;
    private readonly CollectorOptions _options;
    private readonly ConcurrentDictionary<(string Device, string Source), DateTimeOffset> _next = new();
    private readonly ConcurrentDictionary<(string Device, string Source), int> _failures = new();
    private readonly ConcurrentDictionary<string, byte> _active = new(StringComparer.Ordinal);

    /// <summary>Receives generation-owned transport, cache and polling limits.</summary>
    /// <param name="probe">Fixed read-only transport.</param>
    /// <param name="state">Bounded live observation cache.</param>
    /// <param name="options">Immutable generation limits.</param>
    public DeviceDiagnosticsCollector(PlayerProbe probe, CollectorState state, CollectorOptions options)
    {
        this._probe = probe;
        this._state = state;
        this._options = options;
    }

    /// <summary>Collects due sources independently after the caller confirms the device policy.</summary>
    /// <param name="description">Validated identity and supported local service paths.</param>
    /// <param name="address">Approved literal address.</param>
    /// <param name="interval">Effective diagnostic polling interval.</param>
    /// <param name="cancellationToken">Shared bounded sweep cancellation.</param>
    public async Task CollectAsync(PlayerDescription description, string address, TimeSpan interval, CancellationToken cancellationToken)
    {
        if (this._active.TryAdd(description.DeviceId, 0) == false)
        {
            return;
        }
        try
        {
            await this.RunAsync(description.DeviceId, "ifconfig", interval, async () =>
            {
                string body = await this.ReadAsync(token => this._probe.GetAsync(address, "/status/ifconfig", token), cancellationToken);
                var counters = IfconfigParser.Parse(body);
                if (counters.Count == 0)
                {
                    throw new FormatException("No supported interface counters.");
                }
                this._state.InterfaceSucceeded(description.DeviceId, counters, DateTimeOffset.UtcNow);
            }, cancellationToken);
            await this.RunAsync(description.DeviceId, "wireless", interval, async () =>
            {
                string body = await this.ReadAsync(token => this._probe.GetAsync(address, "/status/wireless", token), cancellationToken);
                this._state.WirelessSucceeded(description.DeviceId, WirelessStatusParser.Parse(body), DateTimeOffset.UtcNow);
            }, cancellationToken);
            await this.RunAsync(description.DeviceId, "ethernet", interval, async () =>
            {
                string body = await this.ReadAsync(token => this._probe.GetAsync(address, "/status/enetports", token), cancellationToken);
                this._state.EthernetSucceeded(description.DeviceId, EthernetPortsParser.Parse(body), DateTimeOffset.UtcNow);
            }, cancellationToken);
            if (description.SupportsPlayback)
            {
                await this.RunAsync(description.DeviceId, "playback", interval, async () =>
                {
                    string body = await this.ReadAsync(token => this._probe.GetTransportInfoAsync(address, token), cancellationToken);
                    this._state.PlaybackSucceeded(description.DeviceId, TransportInfoParser.Parse(body), DateTimeOffset.UtcNow);
                }, cancellationToken);
            }
            else
            {
                this._state.DiagnosticFailed(description.DeviceId, "playback", DateTimeOffset.UtcNow, "not_supported");
            }
            if (description.SupportsVolume)
            {
                await this.RunAsync(description.DeviceId, "volume", interval, async () =>
                {
                    string volume = await this.ReadAsync(token => this._probe.GetVolumeAsync(address, token), cancellationToken);
                    string mute = await this.ReadAsync(token => this._probe.GetMuteAsync(address, token), cancellationToken);
                    this._state.VolumeSucceeded(description.DeviceId, RenderingControlParser.ParseVolume(volume),
                        RenderingControlParser.ParseMute(mute), DateTimeOffset.UtcNow);
                }, cancellationToken);
            }
            else
            {
                this._state.DiagnosticFailed(description.DeviceId, "volume", DateTimeOffset.UtcNow, "not_supported");
            }
        }
        finally
        {
            this._active.TryRemove(description.DeviceId, out _);
        }
    }

    /// <summary>Executes one due source while isolating its failure from the remaining sources.</summary>
    /// <param name="id">Validated player identity.</param>
    /// <param name="source">Application-owned source name.</param>
    /// <param name="interval">Effective diagnostic interval.</param>
    /// <param name="read">One source's complete read and validation.</param>
    /// <param name="cancellationToken">Shared sweep cancellation.</param>
    private async Task RunAsync(string id, string source, TimeSpan interval, Func<Task> read, CancellationToken cancellationToken)
    {
        var key = (id, source);
        if (this._next.TryGetValue(key, out var next) && DateTimeOffset.UtcNow < next)
        {
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await read();
            this._failures.TryRemove(key, out _);
            this._next[key] = DateTimeOffset.UtcNow + interval;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or InvalidDataException or
            FormatException or OverflowException or RegexMatchTimeoutException)
        {
            string code = exception switch
            {
                HttpRequestException http when http.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => "not_supported",
                OperationCanceledException => "timeout",
                InvalidDataException => "response_too_large",
                HttpRequestException or IOException => "http_failure",
                _ => "invalid_response"
            };
            this._state.DiagnosticFailed(id, source, DateTimeOffset.UtcNow, code);
            int failures = this._failures.AddOrUpdate(key, 1, (_, count) => Math.Min(5, count + 1));
            double delay = code == "not_supported" ? Math.Max(interval.TotalSeconds, this._options.DiscoveryInterval.TotalSeconds) :
                Math.Max(interval.TotalSeconds, Math.Min(300, this._options.PollInterval.TotalSeconds * Math.Pow(2, failures)));
            this._next[key] = DateTimeOffset.UtcNow.AddSeconds(delay + Random.Shared.NextDouble());
        }
    }

    /// <summary>Retries a transient transport failure once within the shared collection deadline.</summary>
    /// <param name="request">Fixed application-owned read request.</param>
    /// <param name="cancellationToken">Shared collection cancellation.</param>
    /// <returns>A bounded source response.</returns>
    private async Task<string> ReadAsync(Func<CancellationToken, Task<string>> request, CancellationToken cancellationToken)
    {
        try
        {
            return await request(cancellationToken);
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested == false &&
            (exception is OperationCanceledException ||
             exception is HttpRequestException http && (http.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)http.StatusCode >= 500)))
        {
            double requested = (exception as PlayerHttpException)?.RetryDelay?.TotalMilliseconds ?? 100;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(requested, 100, 2000) + Random.Shared.Next(0, 101)), cancellationToken);
            return await request(cancellationToken);
        }
    }
}
