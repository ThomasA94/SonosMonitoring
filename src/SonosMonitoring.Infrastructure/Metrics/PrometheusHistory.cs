using System.Text.Json;
using SonosMonitoring.Application.Metrics;

namespace SonosMonitoring.Infrastructure.Metrics;

/// <summary>Executes bounded read-only Prometheus requests without retrying or buffering missing measurements.</summary>
public sealed class PrometheusHistory : IMetricHistory
{
    private readonly HttpClient _client;

    /// <summary>Receives the configured infrastructure client.</summary>
    /// <param name="client">Client with a fixed base address and timeout.</param>
    public PrometheusHistory(HttpClient client) => this._client = client;

    /// <inheritdoc />
    public async Task<JsonElement> ReadAsync(MetricHistoryRequest request, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await this._client.GetAsync(PrometheusQuery.RelativeUrl(request), HttpCompletionOption.ResponseHeadersRead, budget.Token);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, budget.Token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(budget.Token), new JsonDocumentOptions { MaxDepth = 32 });
        if (document.RootElement.TryGetProperty("status", out var status) == false || status.GetString() != "success" ||
            document.RootElement.TryGetProperty("data", out _) == false)
        {
            throw new HttpRequestException("The history source did not complete the query.");
        }
        return document.RootElement.GetProperty("data").Clone();
    }
}
