using System.Net.Http.Json;
using System.Text.Json;

namespace SonosMonitoring.Web.Connection;

/// <summary>Provides bounded HTTP transport without retrying a business action implicitly.</summary>
public sealed class MonitoringApiClient
{
    private readonly HttpClient _client;

    /// <summary>Receives the configured API-only client and its server-side credential.</summary>
    /// <param name="client">Client with fixed address, timeout, redirect policy and response limit.</param>
    public MonitoringApiClient(HttpClient client) => this._client = client;

    /// <summary>Reads one relative API resource.</summary>
    /// <typeparam name="T">Expected contract.</typeparam>
    /// <param name="path">Application-owned relative API path.</param>
    /// <param name="ct">Page lifetime cancellation.</param>
    public Task<ApiResult<T>> GetAsync<T>(string path, CancellationToken ct = default) =>
        this.SendAsync<T>(new HttpRequestMessage(HttpMethod.Get, path), ct);

    /// <summary>Submits one immutable action with its stable retry identity.</summary>
    /// <typeparam name="T">Expected receipt.</typeparam>
    /// <param name="path">Application-owned command path.</param>
    /// <param name="body">Original command body.</param>
    /// <param name="requestId">Identity retained across ambiguous retries.</param>
    /// <param name="ct">Page lifetime cancellation.</param>
    public Task<ApiResult<T>> PostAsync<T>(string path, object body, Guid requestId, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", requestId.ToString("D"));
        return this.SendAsync<T>(request, ct);
    }

    /// <summary>Downloads the existing reduced export without reconstructing it from live identifiers.</summary>
    /// <param name="ct">Request cancellation.</param>
    public async Task<ApiResult<byte[]>> DownloadAsync(CancellationToken ct)
    {
        try
        {
            using var response = await this._client.GetAsync("api/collection/export", ct);
            return response.IsSuccessStatusCode ? new(await response.Content.ReadAsByteArrayAsync(ct), (int)response.StatusCode, null) :
                new(null, (int)response.StatusCode, "request.failed");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new(null, 0, "connection.failed");
        }
    }

    /// <summary>Decodes only expected data and safe ProblemDetails codes, never exception text.</summary>
    /// <typeparam name="T">Expected response contract.</typeparam>
    /// <param name="request">Owned outgoing message.</param>
    /// <param name="ct">Operation cancellation.</param>
    private async Task<ApiResult<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using (request)
        {
            try
            {
                using var response = await this._client.SendAsync(request, ct);
                if (response.IsSuccessStatusCode && (response.StatusCode != System.Net.HttpStatusCode.Accepted || request.Method == HttpMethod.Post))
                {
                    var value = await response.Content.ReadFromJsonAsync<T>(ct);
                    return new(value, (int)response.StatusCode, value is null ? "response.invalid" : null);
                }
                string code = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Accepted => "projection.pending",
                    System.Net.HttpStatusCode.Forbidden => "access.denied",
                    System.Net.HttpStatusCode.Conflict => "request.conflict",
                    System.Net.HttpStatusCode.NotFound => "request.not_found",
                    _ => "request.failed",
                };
                try
                {
                    using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    if (problem.RootElement.TryGetProperty("code", out var value) || problem.RootElement.TryGetProperty("title", out value))
                    {
                        string? supplied = value.GetString();
                        if (supplied is { Length: < 100 } && supplied.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_'))
                        {
                            code = supplied;
                        }
                    }
                }
                catch (JsonException)
                {
                    // A gateway may return HTML; retain the safe status-derived category.
                }
                return new(default, (int)response.StatusCode, code);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                return new(default, 0, exception is JsonException ? "response.invalid" : "connection.failed");
            }
        }
    }
}
