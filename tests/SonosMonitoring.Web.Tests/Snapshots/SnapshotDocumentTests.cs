using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SonosMonitoring.Contracts.Collection;
using SonosMonitoring.Web.Snapshots;

namespace SonosMonitoring.Web.Tests.Snapshots;

/// <summary>Checks that unsupported and structurally hostile uploads cannot enter the offline view.</summary>
public sealed class SnapshotDocumentTests
{
    /// <summary>Valid documents retain capture time and observations without changing any live source.</summary>
    [Fact]
    public async Task Valid_reduced_document_is_read_as_detached_data()
    {
        var snapshot = new CollectorSnapshotDto(false, null, null, [], [], false);
        var document = new CollectorSnapshotExportDto("sonosmonitoring.collection-snapshot", 1, DateTimeOffset.UtcNow, "reduced-identifiers-v1", snapshot);
        using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var read = await SnapshotDocument.ReadAsync(stream);
        Assert.Equal(document.CapturedAtUtc, read.CapturedAtUtc);
        Assert.Empty(read.Snapshot.Devices);
    }

    /// <summary>Missing nested data and future schemas produce an ordinary validation failure.</summary>
    /// <param name="json">Unsupported file content.</param>
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"format\":\"sonosmonitoring.collection-snapshot\",\"schemaVersion\":2}")]
    [InlineData("{\"format\":\"sonosmonitoring.collection-snapshot\",\"schemaVersion\":1,\"privacy\":\"reduced-identifiers-v1\",\"capturedAtUtc\":\"2026-09-29T12:00:00Z\",\"snapshot\":{\"devices\":[null],\"ranges\":[]}}")]
    public async Task Invalid_document_is_rejected(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await Assert.ThrowsAsync<JsonException>(() => SnapshotDocument.ReadAsync(stream));
    }

    /// <summary>New observation fields survive offline import while older documents use safe unknown defaults.</summary>
    [Fact]
    public async Task Extended_and_legacy_device_documents_remain_readable()
    {
        var node = ExtendedDocument();
        using var extended = new MemoryStream(Encoding.UTF8.GetBytes(node.ToJsonString()));
        var read = await SnapshotDocument.ReadAsync(extended);
        Assert.Equal(24, read.Snapshot.Devices[0].Volume);
        Assert.Equal("PLAYING", read.Snapshot.Devices[0].Playback!.TransportState);
        Assert.Null(read.Snapshot.Devices[0].InterfaceRates[0].ReceiveBytesPerSecond);
        var device = node["snapshot"]!["devices"]![0]!.AsObject();
        foreach (string property in new[] { "wirelessStatus", "ethernetStatus", "playbackStatus", "volumeStatus", "wireless", "ethernetPorts", "playback", "volume", "muted", "interfaceRates", "effectiveIntervalSeconds" })
        {
            device.Remove(property);
        }
        using var legacy = new MemoryStream(Encoding.UTF8.GetBytes(node.ToJsonString()));
        var older = await SnapshotDocument.ReadAsync(legacy);
        Assert.Empty(older.Snapshot.Devices[0].InterfaceRates);
        Assert.Null(older.Snapshot.Devices[0].Volume);
        Assert.Null(older.Snapshot.Devices[0].WirelessStatus.LastSuccessUtc);
    }

    /// <summary>Hostile new numeric observations cannot enter the offline presentation.</summary>
    /// <param name="property">Device property to corrupt.</param>
    /// <param name="value">Invalid numeric value.</param>
    [Theory]
    [InlineData("volume", 101)]
    [InlineData("volume", -1)]
    [InlineData("effectiveIntervalSeconds", 0)]
    [InlineData("probeDurationSeconds", -1)]
    public async Task Invalid_extended_observation_is_rejected(string property, int value)
    {
        var node = ExtendedDocument();
        node["snapshot"]!["devices"]![0]![property] = value;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(node.ToJsonString()));
        await Assert.ThrowsAsync<JsonException>(() => SnapshotDocument.ReadAsync(stream));
    }

    /// <summary>Builds deterministic, synthetic observations without physical device identifiers.</summary>
    private static JsonNode ExtendedDocument()
    {
        var source = new SourceSnapshotDto("description", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false, null);
        var device = new DeviceSnapshotDto("TEST", "192.0.2.1", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, source, source, [])
        {
            Wireless = new("STATION_MODE", "WiFi", false),
            Playback = new("PLAYING", "OK"),
            EthernetPorts = [new("00", false, 0)],
            Volume = 24,
            Muted = false,
            EffectiveIntervalSeconds = 15,
            InterfaceRates = [new("br0", null, 200, DateTimeOffset.UtcNow, 15)],
        };
        var document = new CollectorSnapshotExportDto("sonosmonitoring.collection-snapshot", 1, DateTimeOffset.UtcNow,
            "reduced-identifiers-v1", new(false, null, null, [device], [], false));
        return JsonSerializer.SerializeToNode(document, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
