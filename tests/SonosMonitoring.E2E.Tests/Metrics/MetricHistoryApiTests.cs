using System.Net;
using System.Net.Sockets;
using System.Text;
using SonosMonitoring.Api;

namespace SonosMonitoring.E2E.Tests.Metrics;

/// <summary>Checks history API binding for allowlisted source filters without contacting real services.</summary>
public sealed class MetricHistoryApiTests
{
    /// <summary>The sourceFilter query parameter reaches the bounded Prometheus query; injected values are rejected.</summary>
    [Fact]
    public async Task SourceFilterIsBoundAndAllowlisted()
    {
        using var prometheus = new TcpListener(IPAddress.Loopback, 0);
        prometheus.Start();
        int prometheusPort = ((IPEndPoint)prometheus.LocalEndpoint).Port;
        Task<string> requestLine = this.ReadOnePrometheusRequestAsync(prometheus);
        await using var api = ApiHost.Build([], new Dictionary<string, string?>
        {
            ["Environment"] = "Testing",
            ["CollectionSettings:Path"] = Path.Combine(Path.GetTempPath(), "sonos-isolated-" + Guid.NewGuid().ToString("N"), "settings.json"),
            ["Backend:Enabled"] = "false",
            ["Collector:Enabled"] = "false",
            ["Metrics:PrometheusUrl"] = $"http://127.0.0.1:{prometheusPort}/",
        });
        api.Urls.Clear();
        api.Urls.Add("http://127.0.0.1:0");
        await api.StartAsync();
        using var client = new HttpClient { BaseAddress = new(api.Urls.Single()) };
        string bounds = "deviceKey=RINCON_TEST&start=1970-01-01T00%3A00%3A00Z&end=1970-01-01T00%3A05%3A00Z&stepSeconds=15";

        using var accepted = await client.GetAsync($"/api/metrics/history?metric=source_success&{bounds}&sourceFilter=topology");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        string promRequest = await requestLine;
        Assert.Contains("source%3D%22topology%22", promRequest, StringComparison.OrdinalIgnoreCase);

        using var rejected = await client.GetAsync($"/api/metrics/history?metric=source_success&{bounds}&sourceFilter=topology%22%7D%20or%20up");
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await api.StopAsync();
    }

    /// <summary>Reads one local test request and returns an empty successful Prometheus matrix.</summary>
    /// <param name="listener">Loopback-only fake Prometheus listener.</param>
    /// <returns>The first HTTP request line sent by the API.</returns>
    private async Task<string> ReadOnePrometheusRequestAsync(TcpListener listener)
    {
        using TcpClient connection = await listener.AcceptTcpClientAsync();
        await using NetworkStream stream = connection.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        string line = await reader.ReadLineAsync() ?? string.Empty;
        while (string.IsNullOrEmpty(await reader.ReadLineAsync()) == false)
        {
        }

        byte[] payload = Encoding.UTF8.GetBytes("{\"status\":\"success\",\"data\":{\"resultType\":\"matrix\",\"result\":[]}}");
        byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers);
        await stream.WriteAsync(payload);
        return line;
    }
}
