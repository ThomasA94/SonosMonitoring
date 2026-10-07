using System.Text.Json;
using SonosMonitoring.Contracts.Metrics;
using SonosMonitoring.Web.Metrics;

namespace SonosMonitoring.Web.Tests.Metrics;

/// <summary>Protects measurement units, interface separation and visible gaps in chart geometry.</summary>
public sealed class MetricMatrixTests
{
    /// <summary>Nonfinite and absent samples split the line instead of becoming a zero or a bridge.</summary>
    [Fact]
    public void Missing_samples_and_nan_split_the_path_on_the_actual_time_axis()
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            resultType = "matrix",
            result = new[]
            {
                new { metric = new { @interface = "eth0" }, values = new object[][] { [0, "2"], [15, "4"], [30, "NaN"], [45, "6"], [90, "8"] } },
                new { metric = new { @interface = "wlan0" }, values = new object[][] { [0, "7"] } },
            },
        });
        var response = new MetricHistoryResponse("receive_rate", "RINCON_TEST", "bytes/second", 15,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(90), data);
        var series = MetricMatrix.Parse(response);
        Assert.Equal(2, series.Count);
        Assert.Equal("interface=eth0", series[0].Label);
        Assert.Null(series[0].Points[2].Value);
        string path = MetricMatrix.Path(series[0], response, 10);
        Assert.Equal(3, path.Count(character => character == 'M'));
        Assert.Single(path, character => character == 'L');
        Assert.Contains("M880,64", path);
        Assert.Equal("bytes/second", response.Unit);
    }

    /// <summary>Unexpected result types are rejected rather than displayed as valid empty data.</summary>
    [Fact]
    public void Non_matrix_payload_is_rejected()
    {
        var response = new MetricHistoryResponse("probe_success", "RINCON_TEST", "boolean", 15,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), JsonSerializer.SerializeToElement(new { resultType = "vector", result = Array.Empty<object>() }));
        Assert.Throws<JsonException>(() => MetricMatrix.Parse(response));
    }
}
