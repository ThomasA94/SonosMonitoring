namespace SonosMonitoring.Infrastructure.Collection.Configuration;

/// <summary>Validates collector settings against constraints imposed by optional history services.</summary>
public interface ICollectorHistoryConfigurationGuard
{
    /// <summary>Rejects settings that the active observation-history writer cannot persist.</summary>
    /// <param name="options">Effective collector settings, including loaded or proposed values.</param>
    void Validate(CollectorOptions options);
}
