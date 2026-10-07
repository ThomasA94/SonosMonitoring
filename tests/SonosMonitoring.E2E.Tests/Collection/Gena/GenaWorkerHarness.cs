using Microsoft.Extensions.Logging;
using SonosMonitoring.Infrastructure.Collection;
using SonosMonitoring.Infrastructure.Collection.Gena;

namespace SonosMonitoring.E2E.Tests.Collection.Gena;

/// <summary>Creates and owns the isolated collector objects required by GENA worker journey tests.</summary>
internal sealed class GenaWorkerHarness : IAsyncDisposable
{
    /// <summary>Stable simulator identity shared by the worker fixture and topology publisher.</summary>
    internal const string DEVICE_ID = "RINCON_TEST";

    private readonly CollectorOptions _options;
    private readonly CollectorState _state;
    private readonly PlayerProbe _probe;
    private readonly GenaTransport _transport;
    private readonly GenaWorker _worker;
    private readonly ILoggerFactory _logging = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddConsole());
    private int _started;

    /// <summary>Creates a loopback-only worker with a seeded, explicitly supported player.</summary>
    /// <param name="initialNotificationTimeout">Time allowed for the first callback before lease replacement.</param>
    public GenaWorkerHarness(TimeSpan? initialNotificationTimeout = null)
    {
        this._options = new CollectorOptions
        {
            Enabled = true,
            AllowLoopback = true,
            AllowedCidrs = ["127.0.0.6/32"],
            RequestTimeout = TimeSpan.FromSeconds(1),
            SweepTimeout = TimeSpan.FromSeconds(5),
            PollInterval = TimeSpan.FromSeconds(1),
            DiscoveryInterval = TimeSpan.FromSeconds(5),
            Gena = new GenaOptions
            {
                Enabled = true,
                ListenAddress = "127.0.0.1",
                CallbackAddress = "127.0.0.1",
                Port = 0,
                RequestedLeaseSeconds = 30,
                InitialNotificationTimeout = initialNotificationTimeout ?? TimeSpan.FromSeconds(15),
                MinimumPollInterval = TimeSpan.FromSeconds(1)
            }
        };
        Ipv4TargetPolicy policy = new(this._options.AllowedCidrs, allowLoopback: true);
        this._state = new CollectorState(this._options);
        this._state.DescriptionSucceeded(new PlayerDescription(DEVICE_ID, "Fixture", "Loopback")
        {
            SupportsTopologyEvents = true
        }, "127.0.0.6", DateTimeOffset.UtcNow);
        this._probe = new PlayerProbe(policy, this._options);
        this._transport = new GenaTransport(policy, this._options);
        TopologyCollector topology = new(this._probe, this._state, this._options);
        this._worker = new GenaWorker(this._options, this._state, this._transport, topology, this._logging.CreateLogger<GenaWorker>());
    }

    /// <summary>Starts listener and subscription processing.</summary>
    public async Task StartAsync()
    {
        await this._worker.StartAsync(CancellationToken.None);
        Volatile.Write(ref this._started, 1);
    }

    /// <summary>Stops the worker and waits for its bounded unsubscribe cleanup.</summary>
    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref this._started, 0) == 1)
        {
            await this._worker.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Gets the seeded device's latest collector evidence.</summary>
    public SonosMonitoring.Application.Collection.DeviceSnapshot Device() =>
        Assert.Single(this._state.GetSnapshot().Devices);

    /// <summary>Stops the worker and disposes its owned HTTP clients.</summary>
    public async ValueTask DisposeAsync()
    {
        await this.StopAsync();
        this._probe.Dispose();
        this._transport.Dispose();
        this._worker.Dispose();
        this._logging.Dispose();
    }
}
