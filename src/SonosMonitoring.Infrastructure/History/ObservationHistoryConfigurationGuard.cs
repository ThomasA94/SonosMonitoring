using SonosMonitoring.Infrastructure.Collection.Configuration;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Infrastructure.History;

/// <summary>Applies observation-history capacity constraints to effective collector settings.</summary>
public sealed class ObservationHistoryConfigurationGuard : ICollectorHistoryConfigurationGuard
{
    /// <inheritdoc />
    public void Validate(CollectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxKnownDevices > ObservationHistoryLimits.MAX_KNOWN_DEVICES)
        {
            throw new ArgumentException(
                $"Observation history supports at most {ObservationHistoryLimits.MAX_KNOWN_DEVICES} known collector devices.",
                nameof(options));
        }
    }
}
