using SonosMonitoring.Application.Collection;
using SonosMonitoring.Application.MediaControl.Transport;
using SonosMonitoring.Infrastructure.Collection;
using SonosMonitoring.Infrastructure.MediaControl.Transport;

namespace SonosMonitoring.Unit.Tests.MediaControl.Transport;

/// <summary>Checks the read/prepare/send boundary with a fully simulated single-player household.</summary>
public sealed class MediaDeviceGatewayTests
{
    /// <summary>Refuses queue-only crossfade for a different source even when the static service advertises it.</summary>
    /// <param name="uri">Simulated current source URI.</param>
    [Theory]
    [InlineData("x-sonosapi-stream:station")]
    [InlineData("x-sonos-htastream:RINCON_ABC:spdif")]
    [InlineData("x-rincon-stream:RINCON_ABC")]
    public async Task CurrentSourceCapabilityIsRequiredForCrossfade(string uri)
    {
        using SimulatedMediaPlayer handler = new() { CurrentUri = uri };
        using MediaDeviceGateway gateway = MediaDeviceGatewayTests.CreateGateway(handler);
        MediaPlayback playback = await gateway.ReadAsync("RINCON_ABC", CancellationToken.None);
        Assert.DoesNotContain("crossfade", playback.AvailableActions);
        MediaDeviceException refusal = await Assert.ThrowsAsync<MediaDeviceException>(() => gateway.PrepareAsync(
            "RINCON_ABC", "crossfade", playback.Context, new MediaWriteInputs(Value: 1), CancellationToken.None));
        Assert.Equal("action_unavailable", refusal.Code);
        Assert.Equal(0, handler.Writes);
    }

    /// <summary>Read and preparation issue no writes; one claimed step produces one SOAP write.</summary>
    [Fact]
    public async Task PreparationIsPureAndDispatchMakesOneWrite()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SourceSnapshot source = new("description", true, now, now, false, null);
        DeviceSnapshot device = new("RINCON_ABC", "127.0.0.1", "Study", "Sonos", now, now,
            source, source, [])
        { TopologyStatus = source };
        CollectorSnapshot snapshot = new(true, now, now, [device], [], false) { HeartbeatMaxAgeSeconds = 30 };
        CollectorOptions options = new()
        {
            Enabled = true,
            AllowedCidrs = ["127.0.0.0/8"],
            AllowLoopback = true,
            RequestTimeout = TimeSpan.FromSeconds(1),
            StaleAfter = TimeSpan.FromMinutes(1)
        };
        using SimulatedMediaPlayer handler = new();
        using MediaDeviceGateway gateway = new(new MediaFixedSnapshotReader(snapshot), options, handler);
        MediaPlayback current = await gateway.ReadAsync("RINCON_ABC", CancellationToken.None);
        Assert.Equal("HH_1", current.Context.HouseholdId);
        Assert.Equal(22, current.Volume);
        Assert.Equal(180, current.DurationSeconds);
        IReadOnlyList<MediaWriteStep> steps = await gateway.PrepareAsync("RINCON_ABC", "volume", current.Context,
            new MediaWriteInputs(Value: 33), CancellationToken.None);
        MediaWriteStep step = Assert.Single(steps);
        Assert.Equal("SetVolume", step.Action);
        Assert.Equal(0, handler.Writes);
        await gateway.SendAsync(step, CancellationToken.None);
        Assert.Equal(1, handler.Writes);
        IReadOnlyList<MediaWriteStep> groupSteps = await gateway.PrepareAsync("RINCON_ABC", "group-volume", current.Context,
            new MediaWriteInputs(Value: 40), CancellationToken.None);
        Assert.Equal(new[] { "SnapshotGroupVolume", "SetGroupVolume" }, groupSteps.Select(item => item.Action).ToArray());
        Assert.Equal(1, handler.Writes);
        await gateway.SendAsync(groupSteps[0], CancellationToken.None);
        await gateway.SendAsync(groupSteps[1], CancellationToken.None);
        Assert.True(await gateway.ConfirmAsync("group-volume", current.Context, new MediaWriteInputs(Value: 40), CancellationToken.None));
        Assert.Equal(40, (await gateway.ReadAsync("RINCON_ABC", CancellationToken.None)).GroupVolume);
    }

    /// <summary>Resolves a queue reference to its captured index under the same browse revision.</summary>
    [Fact]
    public async Task QueuePlayUsesOpaqueReferenceAndRejectsChangedRevision()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SourceSnapshot source = new("description", true, now, now, false, null);
        DeviceSnapshot device = new("RINCON_ABC", "127.0.0.1", "Study", "Sonos", now, now,
            source, source, [])
        { TopologyStatus = source };
        CollectorSnapshot snapshot = new(true, now, now, [device], [], false) { HeartbeatMaxAgeSeconds = 30 };
        CollectorOptions options = new()
        {
            Enabled = true,
            AllowedCidrs = ["127.0.0.0/8"],
            AllowLoopback = true,
            RequestTimeout = TimeSpan.FromSeconds(1),
            StaleAfter = TimeSpan.FromMinutes(1)
        };
        using SimulatedMediaPlayer handler = new();
        using MediaDeviceGateway gateway = new(new MediaFixedSnapshotReader(snapshot), options, handler);
        MediaPlayback playback = await gateway.ReadAsync("RINCON_ABC", CancellationToken.None);
        MediaPage page = await gateway.BrowseAsync("RINCON_ABC", "queue", 0, 10, CancellationToken.None);
        MediaItem item = Assert.Single(page.Items);
        IReadOnlyList<MediaWriteStep> steps = await gateway.PrepareAsync("RINCON_ABC", "queue-play",
            playback.Context with { QueueRevision = page.Revision }, new MediaWriteInputs(Reference: item.Reference),
            CancellationToken.None);
        Assert.Equal(new[] { "SetAVTransportURI", "Seek", "Play" }, steps.Select(step => step.Action).ToArray());
        Assert.Equal("1", steps[1].Arguments["Target"]);
        Assert.Equal(0, handler.Writes);
        await Assert.ThrowsAsync<MediaDeviceException>(() => gateway.PrepareAsync("RINCON_ABC", "queue-play",
            playback.Context with { QueueRevision = "4" }, new MediaWriteInputs(Reference: item.Reference), CancellationToken.None));
        IReadOnlyList<MediaWriteStep> mode = await gateway.PrepareAsync("RINCON_ABC", "mode", playback.Context,
            new MediaWriteInputs(Mode: "shuffle-repeat"), CancellationToken.None);
        Assert.Equal("SHUFFLE", Assert.Single(mode).Arguments["NewPlayMode"]);
    }

    /// <summary>Refuses a prepared step when the same address later serves another device identity.</summary>
    [Fact]
    public async Task IdentityChangeBeforeSendPreventsWrite()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SourceSnapshot source = new("description", true, now, now, false, null);
        DeviceSnapshot device = new("RINCON_ABC", "127.0.0.1", "Study", "Sonos", now, now,
            source, source, [])
        { TopologyStatus = source };
        CollectorSnapshot snapshot = new(true, now, now, [device], [], false) { HeartbeatMaxAgeSeconds = 30 };
        CollectorOptions options = new()
        {
            Enabled = true,
            AllowedCidrs = ["127.0.0.0/8"],
            AllowLoopback = true,
            RequestTimeout = TimeSpan.FromSeconds(1),
            StaleAfter = TimeSpan.FromMinutes(1)
        };
        using SimulatedMediaPlayer handler = new();
        using MediaDeviceGateway gateway = new(new MediaFixedSnapshotReader(snapshot), options, handler);
        MediaPlayback playback = await gateway.ReadAsync("RINCON_ABC", CancellationToken.None);
        MediaWriteStep step = Assert.Single(await gateway.PrepareAsync("RINCON_ABC", "volume", playback.Context,
            new MediaWriteInputs(Value: 33), CancellationToken.None));
        handler.DescribedDeviceKey = "RINCON_OTHER";
        await Assert.ThrowsAsync<MediaDeviceException>(() => gateway.SendAsync(step, CancellationToken.None));
        Assert.Equal(0, handler.Writes);
    }

    /// <summary>Refuses dispatch if the reporter no longer places the selected player in its group.</summary>
    [Fact]
    public async Task TopologyChangeBeforeSendPreventsWrite()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SourceSnapshot source = new("description", true, now, now, false, null);
        DeviceSnapshot device = new("RINCON_ABC", "127.0.0.1", "Study", "Sonos", now, now,
            source, source, [])
        { TopologyStatus = source };
        CollectorSnapshot snapshot = new(true, now, now, [device], [], false) { HeartbeatMaxAgeSeconds = 30 };
        CollectorOptions options = new()
        {
            Enabled = true,
            AllowedCidrs = ["127.0.0.0/8"],
            AllowLoopback = true,
            RequestTimeout = TimeSpan.FromSeconds(1),
            StaleAfter = TimeSpan.FromMinutes(1)
        };
        using SimulatedMediaPlayer handler = new();
        using MediaDeviceGateway gateway = new(new MediaFixedSnapshotReader(snapshot), options, handler);
        MediaPlayback playback = await gateway.ReadAsync("RINCON_ABC", CancellationToken.None);
        MediaWriteStep step = Assert.Single(await gateway.PrepareAsync("RINCON_ABC", "volume", playback.Context,
            new MediaWriteInputs(Value: 33), CancellationToken.None));
        handler.ReportedDeviceKey = "RINCON_OTHER";
        await Assert.ThrowsAsync<MediaDeviceException>(() => gateway.SendAsync(step, CancellationToken.None));
        Assert.Equal(0, handler.Writes);
    }

    /// <summary>Sends all three queue playback steps across their planned source transition.</summary>
    [Fact]
    public async Task QueuePlaySendsThreeClaimedStepsAcrossSourceTransition()
    {
        using SimulatedMediaPlayer handler = new() { CurrentUri = "x-sonosapi-stream:old" };
        using MediaDeviceGateway gateway = MediaDeviceGatewayTests.CreateGateway(handler);
        MediaPlayback playback = await gateway.ReadAsync("RINCON_ABC", CancellationToken.None);
        MediaPage page = await gateway.BrowseAsync("RINCON_ABC", "queue", 0, 10, CancellationToken.None);
        MediaItem item = Assert.Single(page.Items);
        IReadOnlyList<MediaWriteStep> steps = await gateway.PrepareAsync("RINCON_ABC", "queue-play",
            playback.Context with { QueueRevision = page.Revision }, new MediaWriteInputs(Reference: item.Reference),
            CancellationToken.None);
        Assert.Equal(0, handler.Writes);
        foreach (MediaWriteStep step in steps)
        {
            await gateway.SendAsync(step, CancellationToken.None);
        }
        Assert.Equal(3, handler.Writes);
        Assert.Equal("x-rincon-queue:RINCON_ABC#0", handler.CurrentUri);
    }

    /// <summary>Rejects an obsolete queue revision immediately before dispatch.</summary>
    [Fact]
    public async Task QueueRevisionChangeBeforeSendPreventsWrite()
    {
        using SimulatedMediaPlayer handler = new();
        using MediaDeviceGateway gateway = MediaDeviceGatewayTests.CreateGateway(handler);
        MediaPlayback playback = await gateway.ReadAsync("RINCON_ABC", CancellationToken.None);
        MediaPage page = await gateway.BrowseAsync("RINCON_ABC", "queue", 0, 10, CancellationToken.None);
        MediaWriteStep step = Assert.Single(await gateway.PrepareAsync("RINCON_ABC", "queue-clear",
            playback.Context with { QueueRevision = page.Revision }, new MediaWriteInputs(), CancellationToken.None));
        handler.QueueRevision = "6";
        await Assert.ThrowsAsync<MediaDeviceException>(() => gateway.SendAsync(step, CancellationToken.None));
        Assert.Equal(0, handler.Writes);
    }

    /// <summary>Requires the advertised service action before preparing a queue mutation.</summary>
    [Fact]
    public async Task MissingServiceActionPreventsPreparation()
    {
        using SimulatedMediaPlayer handler = new() { OmittedServiceAction = "RemoveAllTracksFromQueue" };
        using MediaDeviceGateway gateway = MediaDeviceGatewayTests.CreateGateway(handler);
        MediaPlayback playback = await gateway.ReadAsync("RINCON_ABC", CancellationToken.None);
        MediaPage page = await gateway.BrowseAsync("RINCON_ABC", "queue", 0, 10, CancellationToken.None);
        MediaDeviceException failure = await Assert.ThrowsAsync<MediaDeviceException>(() => gateway.PrepareAsync(
            "RINCON_ABC", "queue-clear", playback.Context with { QueueRevision = page.Revision },
            new MediaWriteInputs(), CancellationToken.None));
        Assert.Equal("action_unavailable", failure.Code);
        Assert.Equal(0, handler.Writes);
    }

    /// <summary>Requires the current transport action in addition to its static contract.</summary>
    [Fact]
    public async Task MissingCurrentActionPreventsPlaybackPreparation()
    {
        using SimulatedMediaPlayer handler = new() { CurrentTransportActions = "Pause,Stop" };
        using MediaDeviceGateway gateway = MediaDeviceGatewayTests.CreateGateway(handler);
        MediaPlayback playback = await gateway.ReadAsync("RINCON_ABC", CancellationToken.None);
        MediaDeviceException failure = await Assert.ThrowsAsync<MediaDeviceException>(() => gateway.PrepareAsync(
            "RINCON_ABC", "resume", playback.Context, new MediaWriteInputs(), CancellationToken.None));
        Assert.Equal("action_unavailable", failure.Code);
        Assert.Equal(0, handler.Writes);
    }

    /// <summary>Checks exact SOAP fields for unrelated single-step media intentions.</summary>
    /// <param name="action">Named media intention.</param>
    /// <param name="soapAction">Expected whitelisted SOAP action.</param>
    [Theory]
    [InlineData("seek", "Seek")]
    [InlineData("mode", "SetPlayMode")]
    [InlineData("mute", "SetMute")]
    [InlineData("queue-remove", "RemoveTrackRangeFromQueue")]
    public async Task SingleStepActionsUseExpectedProtocol(string action, string soapAction)
    {
        using SimulatedMediaPlayer handler = new();
        using MediaDeviceGateway gateway = MediaDeviceGatewayTests.CreateGateway(handler);
        MediaPlayback playback = await gateway.ReadAsync("RINCON_ABC", CancellationToken.None);
        MediaPage page = await gateway.BrowseAsync("RINCON_ABC", "queue", 0, 10, CancellationToken.None);
        MediaTargetContext context = playback.Context with { QueueRevision = page.Revision };
        MediaWriteInputs inputs = action switch
        {
            "seek" => new MediaWriteInputs(PositionSeconds: 20),
            "mode" => new MediaWriteInputs(Mode: "shuffle-repeat"),
            "queue-remove" => new MediaWriteInputs(Index: 1),
            _ => new MediaWriteInputs()
        };
        MediaWriteStep step = Assert.Single(await gateway.PrepareAsync("RINCON_ABC", action, context, inputs,
            CancellationToken.None));
        Assert.Equal(soapAction, step.Action);
        switch (action)
        {
            case "seek":
                Assert.Equal("REL_TIME", step.Arguments["Unit"]);
                Assert.Equal("00:00:20", step.Arguments["Target"]);
                break;
            case "mode":
                Assert.Equal("SHUFFLE", step.Arguments["NewPlayMode"]);
                break;
            case "mute":
                Assert.Equal("1", step.Arguments["DesiredMute"]);
                break;
            case "queue-remove":
                Assert.Equal("1", step.Arguments["StartingIndex"]);
                Assert.Equal(page.Revision, step.Arguments["UpdateID"]);
                break;
        }
        Assert.Equal(0, handler.Writes);
        await gateway.SendAsync(step, CancellationToken.None);
        Assert.Equal(1, handler.Writes);
    }

    /// <summary>Preserves a favorite's playback metadata while hiding its URI from the page.</summary>
    [Fact]
    public async Task FavoriteReferenceRetainsOriginalPlaybackMetadata()
    {
        using SimulatedMediaPlayer handler = new();
        using MediaDeviceGateway gateway = MediaDeviceGatewayTests.CreateGateway(handler);
        MediaPlayback playback = await gateway.ReadAsync("RINCON_ABC", CancellationToken.None);
        MediaPage page = await gateway.BrowseAsync("RINCON_ABC", "favorites", 0, 10, CancellationToken.None);
        MediaItem item = Assert.Single(page.Items);
        Assert.DoesNotContain("x-sonosapi", item.Reference);
        IReadOnlyList<MediaWriteStep> steps = await gateway.PrepareAsync("RINCON_ABC", "start-favorite",
            playback.Context, new MediaWriteInputs(Reference: item.Reference), CancellationToken.None);
        Assert.Equal("x-sonosapi-stream:station", steps[0].Arguments["CurrentURI"]);
        Assert.Equal("<DIDL-Lite><item id=\"station\"><title>Station metadata</title></item></DIDL-Lite>",
            steps[0].Arguments["CurrentURIMetaData"]);
        foreach (MediaWriteStep step in steps)
        {
            await gateway.SendAsync(step, CancellationToken.None);
        }
        Assert.Equal(2, handler.Writes);
    }

    /// <summary>Creates a gateway against fresh collector evidence and a local fake handler.</summary>
    /// <param name="handler">Simulated player.</param>
    /// <returns>Gateway owned by the caller.</returns>
    private static MediaDeviceGateway CreateGateway(SimulatedMediaPlayer handler)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SourceSnapshot source = new("description", true, now, now, false, null);
        DeviceSnapshot device = new("RINCON_ABC", "127.0.0.1", "Study", "Sonos", now, now,
            source, source, [])
        { TopologyStatus = source };
        CollectorSnapshot snapshot = new(true, now, now, [device], [], false) { HeartbeatMaxAgeSeconds = 30 };
        CollectorOptions options = new()
        {
            Enabled = true,
            AllowedCidrs = ["127.0.0.0/8"],
            AllowLoopback = true,
            RequestTimeout = TimeSpan.FromSeconds(1),
            StaleAfter = TimeSpan.FromMinutes(1)
        };
        return new MediaDeviceGateway(new MediaFixedSnapshotReader(snapshot), options, handler);
    }
}
