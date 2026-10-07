using System.Globalization;
using System.Text.Json;
using SonosMonitoring.Contracts.Metrics;

namespace SonosMonitoring.Web.Metrics;

/// <summary>Parses bounded Prometheus matrices and builds discontinuous, time-proportional chart paths.</summary>
public static class MetricMatrix
{
    /// <summary>Decodes source labels and numeric points without manufacturing zeroes.</summary>
    /// <param name="response">Typed metric history response.</param>
    public static IReadOnlyList<MetricSeries> Parse(MetricHistoryResponse response)
    {
        var result = new List<MetricSeries>();
        if (response.Data.GetProperty("resultType").GetString() != "matrix")
        {
            throw new JsonException("Expected a matrix response.");
        }
        foreach (var series in response.Data.GetProperty("result").EnumerateArray().Take(16))
        {
            string label = string.Join(" · ", series.GetProperty("metric").EnumerateObject()
                .Where(property => property.Name is "interface" or "source" or "instance" or "job")
                .Select(property => property.Name + "=" + property.Value.GetString()));
            var points = new List<MetricPoint>();
            foreach (var sample in series.GetProperty("values").EnumerateArray().Take(11001))
            {
                double timestamp = sample[0].GetDouble();
                var time = DateTimeOffset.FromUnixTimeMilliseconds(checked((long)(timestamp * 1000)));
                double? value = double.TryParse(sample[1].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number) ? number : null;
                points.Add(new(time, value));
            }
            result.Add(new(label, points.OrderBy(point => point.Time).ToArray()));
        }
        return result;
    }

    /// <summary>Splits SVG paths at missing samples and temporal gaps instead of implying measured continuity.</summary>
    /// <param name="series">Single label set.</param>
    /// <param name="response">Query's absolute time bounds and resolution.</param>
    /// <param name="maximum">Shared finite chart maximum.</param>
    public static string Path(MetricSeries series, MetricHistoryResponse response, double maximum)
    {
        var path = new System.Text.StringBuilder();
        DateTimeOffset? previous = null;
        foreach (var point in series.Points)
        {
            if (point.Value is null || point.Time < response.Start || point.Time > response.End)
            {
                previous = null;
                continue;
            }
            double x = 52 + (point.Time - response.Start).TotalSeconds / (response.End - response.Start).TotalSeconds * 828;
            double y = 200 - Math.Clamp(point.Value.Value / Math.Max(1, maximum), 0, 1) * 170;
            string command = previous is null || (point.Time - previous.Value).TotalSeconds > response.StepSeconds * 1.5 ? "M" : "L";
            path.Append(CultureInfo.InvariantCulture, $"{command}{x:0.##},{y:0.##} ");
            previous = point.Time;
        }
        return path.ToString();
    }
}
