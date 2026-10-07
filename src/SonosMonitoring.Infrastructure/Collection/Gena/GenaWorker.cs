using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SonosMonitoring.Infrastructure.Collection.Gena;

/// <summary>Maintains bounded event leases and an isolated callback listener while ordinary polling remains active.</summary>
public sealed class GenaWorker : BackgroundService
{
    private readonly CollectorOptions _options;
    private readonly CollectorState _state;
    private readonly GenaTransport _transport;
    private readonly TopologyCollector _topology;
    private readonly ILogger<GenaWorker> _logger;
    private readonly Dictionary<string, GenaSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, GenaSession> _routes = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _callbacks = new(4);

    /// <summary>Connects the event receiver to existing source isolation and target validation.</summary>
    /// <param name="options">Explicit opt-in and limits.</param>
    /// <param name="state">Admitted devices and source status.</param>
    /// <param name="transport">Allowlisted subscription transport.</param>
    /// <param name="topology">Bounded SOAP reconciliation.</param>
    /// <param name="logger">Logger receiving only bounded operational status.</param>
    public GenaWorker(CollectorOptions options, CollectorState state, GenaTransport transport,
        TopologyCollector topology, ILogger<GenaWorker> logger)
    {
        this._options = options;
        this._state = state;
        this._transport = transport;
        this._topology = topology;
        this._logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (this._options.Gena.Enabled == false)
        {
            return;
        }
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                await this.RunListenerAsync(stoppingToken);
            }
            catch (Exception exception) when (stoppingToken.IsCancellationRequested == false)
            {
                this._logger.LogWarning("GENA listener unavailable ({FailureType}); polling continues and listener startup will be retried.", exception.GetType().Name);
                foreach (var device in this._state.GetSnapshot().Devices)
                {
                    this._state.GenaResult(device.DeviceId, new(true, false, DateTimeOffset.UtcNow, null, null, false, "listener_unavailable"));
                }
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    /// <summary>Starts the listener before subscription and removes every ephemeral route on shutdown.</summary>
    /// <param name="stoppingToken">Host shutdown.</param>
    private async Task RunListenerAsync(CancellationToken stoppingToken)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Production" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [HostDefaults.ApplicationKey] = builder.Environment.ApplicationName,
            [HostDefaults.EnvironmentKey] = builder.Environment.EnvironmentName,
            [HostDefaults.ContentRootKey] = builder.Environment.ContentRootPath
        });
        builder.Logging.ClearProviders(); // Callback URLs contain capabilities and must never enter access logs.
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));
        builder.WebHost.UseUrls($"http://{this._options.Gena.ListenAddress}:{this._options.Gena.Port}");
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.AddServerHeader = false;
            server.Limits.MaxRequestBodySize = this._options.MaxResponseBytes;
            server.Limits.MaxConcurrentConnections = 32;
            server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
        });
        await using var app = builder.Build();
        app.MapMethods("/sonos/events/{token}", ["NOTIFY"], this.NotifyAsync);
        await app.StartAsync(stoppingToken);
        int callbackPort = new Uri(app.Urls.Single()).Port;
        try
        {
            while (stoppingToken.IsCancellationRequested == false)
            {
                var devices = this._state.GetSnapshot().Devices;
                var eligible = devices.Where(device => device.SupportsTopologyEvents && device.Description.LastAttemptSucceeded && device.Description.IsStale == false)
                    .ToDictionary(device => device.DeviceId, StringComparer.Ordinal);
                List<GenaSession> retired = [];
                foreach (var session in this._sessions.Values.ToArray())
                {
                    if (eligible.TryGetValue(session.DeviceId, out var device) == false || device.Address != session.Address ||
                        session.Expired(DateTimeOffset.UtcNow) || session.InitialMissing(DateTimeOffset.UtcNow, this._options.Gena.InitialNotificationTimeout))
                    {
                        this._sessions.Remove(session.DeviceId);
                        this._routes.TryRemove(session.Token, out _);
                        retired.Add(session);
                    }
                }
                using (var cleanup = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
                {
                    cleanup.CancelAfter(this._options.RequestTimeout);
                    try
                    {
                        await Parallel.ForEachAsync(retired, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cleanup.Token },
                            async (session, token) => await this.ReleaseAsync(session, token));
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested == false) { }
                }
                foreach (var device in devices)
                {
                    if (eligible.ContainsKey(device.DeviceId) == false)
                    {
                        this._state.GenaResult(device.DeviceId, new(true, false, null, null, device.Gena.LastNotificationUtc, false,
                            device.SupportsTopologyEvents ? "identity_unconfirmed" : "not_supported"));
                        continue;
                    }
                    if (this._sessions.ContainsKey(device.DeviceId) == false)
                    {
                        GenaSession session = new(device.DeviceId, device.Address);
                        this._sessions.Add(device.DeviceId, session);
                        this._routes.TryAdd(session.Token, session);
                    }
                }
                await Parallel.ForEachAsync(this._sessions.Values.ToArray(), new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(4, this._options.MaxParallelism),
                    CancellationToken = stoppingToken
                }, async (session, token) => await this.UpdateAsync(session, callbackPort, token));
                await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
            }
        }
        finally
        {
            this._routes.Clear();
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await Parallel.ForEachAsync(this._sessions.Values.ToArray(), new ParallelOptions
                {
                    MaxDegreeOfParallelism = 4,
                    CancellationToken = shutdown.Token
                }, async (session, token) => await this.ReleaseAsync(session, token));
            }
            catch (OperationCanceledException) { }
            this._sessions.Clear();
            await app.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Renews or retries one lease, then consumes at most one coalesced topology hint.</summary>
    /// <param name="session">One bounded admitted-player session.</param>
    /// <param name="port">Actual listener port, including an ephemeral isolated test binding.</param>
    /// <param name="cancellationToken">Worker cancellation.</param>
    private async Task UpdateAsync(GenaSession session, int port, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (session.Due(now))
        {
            session.BeginAttempt(now);
            try
            {
                Uri callback = new($"http://{this._options.Gena.CallbackAddress}:{port}/sonos/events/{session.Token}");
                var grant = await this._transport.SubscribeAsync(session.Address, callback, session.Sid, cancellationToken);
                session.Confirm(grant, DateTimeOffset.UtcNow, this._options.Gena.RequestedLeaseSeconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed && session.Sid is not null)
            {
                // Expire this route; the serial owner replaces it before the next attempt.
                session.InvalidateLease(DateTimeOffset.UtcNow);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or FormatException)
            {
                this._logger.LogDebug("GENA subscription failed ({FailureType}, HTTP {StatusCode}).", exception.GetType().Name,
                    (exception as HttpRequestException)?.StatusCode);
                session.Failed(DateTimeOffset.UtcNow);
            }
        }
        this._state.GenaResult(session.DeviceId, session.Snapshot(DateTimeOffset.UtcNow, this._options.Gena.InitialNotificationTimeout));
        if (session.TakePoll(DateTimeOffset.UtcNow, this._options.Gena.MinimumPollInterval))
        {
            this._topology.Invalidate(session.DeviceId);
            await this._topology.CollectAsync(session.DeviceId, session.Address, cancellationToken);
        }
    }

    /// <summary>Accepts only capability-bearing requests from the address whose identity was validated by HTTP.</summary>
    /// <param name="context">Inbound notification on the separate listener.</param>
    private async Task NotifyAsync(HttpContext context)
    {
        string? token = context.Request.RouteValues["token"]?.ToString();
        if (token is null || this._routes.TryGetValue(token, out var session) == false ||
            context.Connection.RemoteIpAddress?.MapToIPv4().ToString() != session.Address)
        {
            context.Response.StatusCode = 412;
            return;
        }
        if (await this._callbacks.WaitAsync(0, context.RequestAborted) == false)
        {
            context.Response.StatusCode = 429;
            return;
        }
        try
        {
            if (new[] { "SID", "NT", "NTS", "SEQ" }.Any(name => context.Request.Headers[name].Count != 1))
            {
                context.Response.StatusCode = 400;
                return;
            }
            if (context.Request.ContentLength > this._options.MaxResponseBytes)
            {
                context.Response.StatusCode = 413;
                return;
            }
            var headers = GenaHeaders.Parse(context.Request.Headers["SID"].ToString(), context.Request.Headers["NT"].ToString(),
                context.Request.Headers["NTS"].ToString(), context.Request.Headers["SEQ"].ToString());
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            budget.CancelAfter(this._options.RequestTimeout);
            using MemoryStream bytes = new();
            byte[] chunk = new byte[8192];
            while (true)
            {
                int count = await context.Request.Body.ReadAsync(chunk, budget.Token);
                if (count == 0)
                { break; }
                if (bytes.Length + count > this._options.MaxResponseBytes)
                {
                    context.Response.StatusCode = 413;
                    return;
                }
                bytes.Write(chunk, 0, count);
            }
            bool changed = GenaNotificationParser.Parse(new UTF8Encoding(false, true).GetString(bytes.ToArray()));
            bool accepted = session.Accept(headers, changed, DateTimeOffset.UtcNow);
            context.Response.StatusCode = accepted ? 200 : 412;
            if (accepted)
            {
                this._state.GenaResult(session.DeviceId, session.Snapshot(DateTimeOffset.UtcNow, this._options.Gena.InitialNotificationTimeout));
            }
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException or BadHttpRequestException or IOException or OperationCanceledException)
        {
            context.Response.StatusCode = 400;
        }
        finally
        {
            this._callbacks.Release();
        }
    }

    /// <summary>Attempts bounded cleanup without turning unreachable players into a collector failure.</summary>
    /// <param name="session">Lease being retired.</param>
    /// <param name="cancellationToken">Removal or shutdown deadline.</param>
    private async Task ReleaseAsync(GenaSession session, CancellationToken cancellationToken)
    {
        if (session.Sid is not string sid)
        { return; }
        try
        { await this._transport.UnsubscribeAsync(session.Address, sid, cancellationToken); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException) { }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        base.Dispose();
        this._callbacks.Dispose();
    }
}
