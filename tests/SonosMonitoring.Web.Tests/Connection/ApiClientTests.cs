using System.Net;
using System.Net.Http.Json;
using SonosMonitoring.Contracts.Monitoring;
using SonosMonitoring.Web.Connection;
using SonosMonitoring.Web.Monitoring;

namespace SonosMonitoring.Web.Tests.Connection;

/// <summary>Verifies response semantics and immutable retry inputs at the HTTP boundary.</summary>
public sealed class ApiClientTests
{
    /// <summary>Projection 202 is a wait state, whereas command 202 contains a durable receipt.</summary>
    [Fact]
    public async Task Accepted_reads_are_pending_and_accepted_commands_decode_receipts()
    {
        using var handler = new StubHandler(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = request.Method == HttpMethod.Get ? JsonContent.Create(new { title = "projection.pending" }) :
                JsonContent.Create(new MonitoringAccepted("stream", "opaque", 2, false, "/api/monitoring/RINCON_TEST?minSequence=2")),
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var api = new MonitoringApiClient(http);
        var read = await api.GetAsync<MonitoringResponse>("api/monitoring/RINCON_TEST");
        Assert.False(read.IsSuccess);
        Assert.Equal("projection.pending", read.ErrorCode);
        var write = await api.PostAsync<MonitoringAccepted>("api/monitoring/RINCON_TEST/enable", new EnableMonitoringRequest(15), Guid.NewGuid());
        Assert.True(write.IsSuccess);
        Assert.Equal("opaque", write.Value!.ConcurrencyToken);
    }

    /// <summary>A lost response cannot change the key, token, route or body of an explicit retry.</summary>
    [Fact]
    public async Task Ambiguous_retry_preserves_the_entire_original_action()
    {
        var requests = new List<(string Key, string Body, string Path)>();
        using var handler = new StubHandler(async request =>
        {
            requests.Add((request.Headers.GetValues("Idempotency-Key").Single(), await request.Content!.ReadAsStringAsync(), request.RequestUri!.AbsolutePath));
            if (requests.Count == 1)
            {
                throw new HttpRequestException("Internal details must not reach the browser.");
            }
            return new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = JsonContent.Create(new MonitoringAccepted("stream", "new-token", 3, true, "/read")),
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var api = new MonitoringApiClient(http);
        var action = new MonitoringAction("RINCON_TEST", true, 37, "original-token");
        var failure = await action.SendAsync(api, CancellationToken.None);
        Assert.Equal("connection.failed", failure.ErrorCode);
        var retried = await action.SendAsync(api, CancellationToken.None);
        Assert.True(retried.Value!.AlreadyCommitted);
        Assert.Equal(requests[0], requests[1]);
        Assert.Contains("37", requests[0].Body);
        Assert.Contains("original-token", requests[0].Body);
    }

    /// <summary>Malformed gateway responses never expose HTML or raw exception details.</summary>
    /// <param name="status">Gateway status being simulated.</param>
    /// <param name="expected">Safe category expected by the UI.</param>
    [Theory]
    [InlineData(403, "access.denied")]
    [InlineData(409, "request.conflict")]
    [InlineData(503, "request.failed")]
    public async Task Html_gateway_failures_keep_safe_categories(int status, string expected)
    {
        using var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("<html>secret traceback</html>") }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var result = await new MonitoringApiClient(http).GetAsync<MonitoringResponse>("api/monitoring/RINCON_TEST");
        Assert.Equal(expected, result.ErrorCode);
        Assert.Null(result.Value);
    }
}
