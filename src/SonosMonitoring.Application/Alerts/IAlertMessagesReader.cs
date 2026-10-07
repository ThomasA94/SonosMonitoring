namespace SonosMonitoring.Application.Alerts;

/// <summary>Reads current internal messages from the configured Prometheus rule evaluator.</summary>
public interface IAlertMessagesReader
{
    /// <summary>Returns current pending and firing messages after validating recent rule evaluation.</summary>
    /// <param name="ct">Request cancellation.</param>
    Task<AlertMessagesSnapshot> ReadAsync(CancellationToken ct);
}
