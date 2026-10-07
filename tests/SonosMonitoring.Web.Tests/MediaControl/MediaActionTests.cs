using System.Net;
using System.Net.Http.Json;
using SonosMonitoring.Contracts.MediaControl;
using SonosMonitoring.Web.Connection;
using SonosMonitoring.Web.MediaControl;
using SonosMonitoring.Web.Tests.Connection;

namespace SonosMonitoring.Web.Tests.MediaControl;

/// <summary>Verifies that a lost command response cannot silently change the intended device action.</summary>
public sealed class MediaActionTests
{
    /// <summary>A retry retains the player, group, source, queue revision, body and operation identity.</summary>
    [Fact]
    public async Task Ambiguous_retry_preserves_original_media_intention()
    {
        var requests = new List<(string Key, string Body, string Path)>();
        using var handler = new StubHandler(async request =>
        {
            requests.Add((request.Headers.GetValues("Idempotency-Key").Single(), await request.Content!.ReadAsStringAsync(), request.RequestUri!.AbsolutePath));
            if (requests.Count == 1)
            {
                throw new HttpRequestException("simulated lost acknowledgement");
            }
            return new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = JsonContent.Create(new MediaOperationDto(Guid.Parse(requests[0].Key), "queue-remove", "RINCON_A", "accepted",
                    DateTimeOffset.UtcNow, null, "opaque", 1)),
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var context = new MediaTargetContextDto("RINCON_A", "household-1", "RINCON_C", ["RINCON_A", "RINCON_C"], "source-1", "queue-7");
        var action = new MediaAction("RINCON_A", "queue-remove", new MediaActionRequest(context, Index: 3));
        var first = await action.SendAsync(new MonitoringApiClient(http), CancellationToken.None);
        Assert.Equal("connection.failed", first.ErrorCode);
        var second = await action.SendAsync(new MonitoringApiClient(http), CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Equal(action.OperationId, second.Value!.OperationId);
        Assert.Equal(requests[0], requests[1]);
        Assert.Contains("queue-7", requests[0].Body);
        Assert.Contains("RINCON_C", requests[0].Body);
        Assert.Equal("/api/media/players/RINCON_A/queue-remove", requests[0].Path);
    }
}
