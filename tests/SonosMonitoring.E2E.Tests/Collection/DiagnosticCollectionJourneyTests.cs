using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SonosMonitoring.Api.Collection;
using SonosMonitoring.Application.Collection;
using SonosMonitoring.Contracts.Collection;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.E2E.Tests.Collection;

/// <summary>Exercises new diagnostic sources through bounded HTTP requests, collector state, and API mappings.</summary>
[Collection("Loopback player ports")]
public sealed class DiagnosticCollectionJourneyTests
{
    /// <summary>Collects every supported diagnostic source and maps fresh values to live and reduced snapshots.</summary>
    [Fact]
    public async Task CollectsAndExportsSupportedDiagnosticSources()
    {
        await using var players = new SimulatedPlayer();
        await players.StartAsync();
        await using DiagnosticTestHost host = await DiagnosticTestHost.StartAsync(new DiagnosticCollectionPolicy(true, 1));

        CollectorSnapshotDto snapshot = await this.UntilAsync(host.Client, current => current.Devices.Count == 2 &&
            current.Devices.All(device => device.WirelessStatus.LastAttemptSucceeded && device.EthernetStatus.LastAttemptSucceeded &&
                device.PlaybackStatus.LastAttemptSucceeded && device.VolumeStatus.LastAttemptSucceeded));

        var first = snapshot.Devices.Single(device => device.DeviceId == "RINCON_FIRST");
        Assert.Equal("86.10-80260", first.SoftwareVersion);
        Assert.Equal("1.20.2.3-2.0", first.HardwareVersion);
        Assert.Equal("A100", first.ModelNumber);
        Assert.Equal("STATION_MODE", first.Wireless?.WifiMode);
        Assert.Equal("WiFi", first.Wireless?.ConnectionType);
        Assert.Equal(false, first.Wireless?.SonosNetDisabled);
        var port = Assert.Single(first.EthernetPorts);
        Assert.Equal("0", port.Port);
        Assert.False(port.LinkUp);
        Assert.Equal(0, port.ReportedSpeed);
        Assert.Equal("STOPPED", first.Playback?.TransportState);
        Assert.Equal("OK", first.Playback?.TransportStatus);
        Assert.Equal(34, first.Volume);
        Assert.Equal(false, first.Muted);
        Assert.Equal(true, first.MonitoringEnabled);
        Assert.Equal(1d, first.EffectiveIntervalSeconds);
        Assert.True(players.DiagnosticRequests("wireless") >= 2);
        Assert.True(players.DiagnosticRequests("ethernet") >= 2);
        Assert.True(players.DiagnosticRequests("playback") >= 2);
        Assert.True(players.DiagnosticRequests("volume") >= 2);
        Assert.True(players.DiagnosticRequests("mute") >= 2);

        var document = await host.Client.GetFromJsonAsync<CollectorSnapshotExportDto>("/api/collection/export");
        Assert.NotNull(document);
        var exported = document.Snapshot.Devices[0];
        Assert.Equal(first.MonitoringEnabled, exported.MonitoringEnabled);
        Assert.Equal(first.EffectiveIntervalSeconds, exported.EffectiveIntervalSeconds);
        Assert.Equal(first.WirelessStatus, exported.WirelessStatus);
        Assert.Equal(first.EthernetStatus, exported.EthernetStatus);
        Assert.Equal(first.PlaybackStatus, exported.PlaybackStatus);
        Assert.Equal(first.VolumeStatus, exported.VolumeStatus);
        Assert.Null(exported.SoftwareVersion);
        Assert.Null(exported.HardwareVersion);
        Assert.Null(exported.ModelNumber);
        Assert.Equal(first.Wireless, exported.Wireless);
        var exportedPort = Assert.Single(exported.EthernetPorts);
        Assert.Equal(port.LinkUp, exportedPort.LinkUp);
        Assert.Equal(port.ReportedSpeed, exportedPort.ReportedSpeed);
        Assert.NotEqual(port.Port, exportedPort.Port);
        Assert.Equal(first.Playback, exported.Playback);
        Assert.Equal(first.Volume, exported.Volume);
        Assert.Equal(first.Muted, exported.Muted);
        await host.App.StopAsync();
    }

    /// <summary>A failing source retains its last good value while the peer and other sources continue updating.</summary>
    /// <param name="source">The source receiving the injected fault.</param>
    /// <param name="fault">The failure returned by the simulator.</param>
    [Theory]
    [InlineData("wireless", "503")]
    [InlineData("wireless", "malformed")]
    [InlineData("wireless", "timeout")]
    [InlineData("wireless", "404")]
    [InlineData("playback", "soap_fault")]
    [InlineData("volume", "partial_volume")]
    [InlineData("volume", "partial_mute")]
    public async Task DiagnosticFailureIsIsolatedAndRetainsTheLastGoodObservation(string source, string fault)
    {
        await using var players = new SimulatedPlayer();
        await players.StartAsync();
        await using DiagnosticTestHost host = await DiagnosticTestHost.StartAsync(new DiagnosticCollectionPolicy(true, 1));
        CollectorSnapshotDto initial = await this.UntilAsync(host.Client, current => current.Devices.Count == 2 &&
            current.Devices.All(device => device.WirelessStatus.LastAttemptSucceeded && device.EthernetStatus.LastAttemptSucceeded &&
                device.PlaybackStatus.LastAttemptSucceeded && device.VolumeStatus.LastAttemptSucceeded));
        DeviceSnapshotDto original = initial.Devices.Single(device => device.DeviceId == "RINCON_SECOND");

        players.SourceFault = source;
        players.DiagnosticFault = fault;
        CollectorSnapshotDto failed = await this.UntilAsync(host.Client, current =>
        {
            DeviceSnapshotDto bad = current.Devices.Single(device => device.DeviceId == "RINCON_SECOND");
            DeviceSnapshotDto peer = current.Devices.Single(device => device.DeviceId == "RINCON_FIRST");
            SourceSnapshotDto badSource = this.Source(bad, source);
            SourceSnapshotDto peerSource = this.Source(peer, source);
            SourceSnapshotDto unaffectedBadSource = source == "volume" ? bad.EthernetStatus : bad.VolumeStatus;
            SourceSnapshotDto unaffectedOriginal = source == "volume" ? original.EthernetStatus : original.VolumeStatus;
            return badSource.LastAttemptSucceeded == false && badSource.IsStale &&
                badSource.LastSuccessUtc == this.Source(original, source).LastSuccessUtc &&
                unaffectedBadSource.LastAttemptSucceeded && unaffectedBadSource.LastSuccessUtc > unaffectedOriginal.LastSuccessUtc &&
                peerSource.LastAttemptSucceeded && peerSource.LastSuccessUtc > this.Source(initial.Devices.Single(device => device.DeviceId == "RINCON_FIRST"), source).LastSuccessUtc;
        });

        DeviceSnapshotDto retained = failed.Devices.Single(device => device.DeviceId == "RINCON_SECOND");
        Assert.Equal(original.Wireless, retained.Wireless);
        Assert.Equal(original.EthernetPorts, retained.EthernetPorts);
        Assert.Equal(original.Playback, retained.Playback);
        Assert.Equal(original.Volume, retained.Volume);
        Assert.Equal(original.Muted, retained.Muted);
        Assert.True(retained.EthernetStatus.LastAttemptSucceeded);
        Assert.True(retained.InterfaceStatus.LastAttemptSucceeded);
        Assert.True(failed.Devices.Single(device => device.DeviceId == "RINCON_FIRST").WirelessStatus.LastAttemptSucceeded);
        Assert.True(failed.Devices.Single(device => device.DeviceId == "RINCON_FIRST").PlaybackStatus.LastAttemptSucceeded);
        await host.App.StopAsync();
    }

    /// <summary>Does not poll unadvertised renderer services and does not invent zeros when policy disables collection.</summary>
    [Fact]
    public async Task UnsupportedRendererSourcesRemainAbsentWithoutDiagnosticRequests()
    {
        await using var players = new SimulatedPlayer { SecondSupportsPlayback = false, SecondSupportsRendering = false };
        await players.StartAsync();
        await using DiagnosticTestHost host = await DiagnosticTestHost.StartAsync(new DiagnosticCollectionPolicy(true, 1));

        CollectorSnapshotDto snapshot = await this.UntilAsync(host.Client, current => current.Devices.Count == 2 &&
            current.Devices.All(device => device.WirelessStatus.LastAttemptSucceeded && device.EthernetStatus.LastAttemptSucceeded));
        DeviceSnapshotDto first = snapshot.Devices.Single(device => device.DeviceId == "RINCON_FIRST");
        DeviceSnapshotDto second = snapshot.Devices.Single(device => device.DeviceId == "RINCON_SECOND");
        Assert.Equal(true, first.MonitoringEnabled);
        Assert.Equal(true, second.MonitoringEnabled);
        Assert.Null(second.Playback);
        Assert.Null(second.Volume);
        Assert.Null(second.Muted);
        Assert.Equal("not_supported", second.PlaybackStatus.ErrorCode);
        Assert.Equal("not_supported", second.VolumeStatus.ErrorCode);
        Assert.Equal(0, players.DiagnosticRequests("playback", "127.0.0.3"));
        Assert.Equal(0, players.DiagnosticRequests("volume", "127.0.0.3"));
        Assert.Equal(0, players.DiagnosticRequests("mute", "127.0.0.3"));
        Assert.True(players.DiagnosticRequests("playback", "127.0.0.2") > 0);
        Assert.True(players.DiagnosticRequests("volume", "127.0.0.2") > 0);
        Assert.True(players.DiagnosticRequests("mute", "127.0.0.2") > 0);
        Assert.True(players.DiagnosticRequests("wireless") > 0);
        Assert.True(players.DiagnosticRequests("ethernet") > 0);
        await host.App.StopAsync();
    }

    /// <summary>Does not send any diagnostics when policy disables monitoring for both players.</summary>
    [Fact]
    public async Task DisabledPolicyPreventsAllDiagnosticRequests()
    {
        await using var players = new SimulatedPlayer();
        await players.StartAsync();
        await using DiagnosticTestHost host = await DiagnosticTestHost.StartAsync(new DiagnosticCollectionPolicy(false, 7));

        CollectorSnapshotDto disabled = await this.UntilAsync(host.Client, current => current.Devices.Count == 2 &&
            current.Devices.All(device => device.MonitoringEnabled == false));
        Assert.All(disabled.Devices, device =>
        {
            Assert.Null(device.Wireless);
            Assert.Empty(device.EthernetPorts);
            Assert.Null(device.Playback);
            Assert.Null(device.Volume);
            Assert.Null(device.Muted);
            Assert.Equal("monitoring_disabled", device.WirelessStatus.ErrorCode);
            Assert.Equal("monitoring_disabled", device.EthernetStatus.ErrorCode);
            Assert.Equal("monitoring_disabled", device.PlaybackStatus.ErrorCode);
            Assert.Equal("monitoring_disabled", device.VolumeStatus.ErrorCode);
            Assert.Equal(7d, device.EffectiveIntervalSeconds);
        });
        Assert.Equal(0, players.DiagnosticRequests("wireless"));
        Assert.Equal(0, players.DiagnosticRequests("ethernet"));
        Assert.Equal(0, players.DiagnosticRequests("playback"));
        Assert.Equal(0, players.DiagnosticRequests("volume"));
        Assert.Equal(0, players.DiagnosticRequests("mute"));
        await host.App.StopAsync();
    }

    /// <summary>Returns the status associated with one fixed diagnostic source.</summary>
    /// <param name="device">The player snapshot.</param>
    /// <param name="source">The bounded source name.</param>
    /// <returns>The requested source status.</returns>
    private SourceSnapshotDto Source(DeviceSnapshotDto device, string source)
    {
        return source switch
        {
            "wireless" => device.WirelessStatus,
            "ethernet" => device.EthernetStatus,
            "playback" => device.PlaybackStatus,
            "volume" => device.VolumeStatus,
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
    }

    /// <summary>Waits for an asynchronously collected HTTP snapshot to satisfy the assertion.</summary>
    /// <param name="client">The API client.</param>
    /// <param name="condition">The observable condition.</param>
    /// <returns>The matching snapshot.</returns>
    private async Task<CollectorSnapshotDto> UntilAsync(HttpClient client, Func<CollectorSnapshotDto, bool> condition)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(45));
        while (true)
        {
            CollectorSnapshotDto? snapshot = await client.GetFromJsonAsync<CollectorSnapshotDto>("/api/collection", deadline.Token);
            if (snapshot is not null && condition(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(100, deadline.Token);
        }
    }

}
