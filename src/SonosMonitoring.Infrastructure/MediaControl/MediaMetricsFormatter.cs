using System.Globalization;
using System.Text;
using SonosMonitoring.Application.MediaControl;

namespace SonosMonitoring.Infrastructure.MediaControl;

/// <summary>Exports installation-wide durable phase counts without identity labels or duplicate executor counters.</summary>
public static class MediaMetricsFormatter
{
    /// <summary>Reads the authoritative store once per scrape with an independent bounded budget.</summary>
    /// <param name="store">Configured media coordinator, or null when the slice is disabled.</param>
    /// <param name="ct">Scrape cancellation.</param>
    /// <returns>Samples whose names are declared by the shared metric catalogue.</returns>
    public static async Task<string> FormatAsync(IMediaCoordinationStore? store, CancellationToken ct)
    {
        StringBuilder output = new();
        output.Append("sonos_media_configured ").Append(store is null ? '0' : '1').Append('\n');
        if (store is null)
        {
            return output.ToString();
        }
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var counts = await store.CountsAsync(budget.Token);
            output.Append("sonos_media_coordination_available 1\n");
            foreach ((string status, long count) in counts.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                output.Append("sonos_media_operations{status=\"").Append(status).Append("\"} ")
                    .Append(count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
        }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or MediaCoordinationException or OperationCanceledException)
        {
            output.Append("sonos_media_coordination_available 0\n");
        }
        return output.ToString();
    }
}
