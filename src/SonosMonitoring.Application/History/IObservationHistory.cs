namespace SonosMonitoring.Application.History;

/// <summary>Reads the durable observational journal independently of the live collector cache.</summary>
public interface IObservationHistory
{
    /// <summary>Reads retained entries in ascending order; device filtering retains global gap markers.</summary>
    /// <param name="after">Exclusive nonnegative sequence cursor.</param>
    /// <param name="limit">Maximum entries, from one through two hundred.</param>
    /// <param name="deviceId">Optional exact device identity filter.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<ObservationPage> ReadAsync(long after, int limit, string? deviceId, CancellationToken cancellationToken);
}
