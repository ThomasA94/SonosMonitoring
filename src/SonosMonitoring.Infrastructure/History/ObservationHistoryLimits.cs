namespace SonosMonitoring.Infrastructure.History;

/// <summary>Defines resource limits shared by observation-history writers and collector guards.</summary>
public static class ObservationHistoryLimits
{
    /// <summary>Maximum number of devices supported by one observation-history sample.</summary>
    public const int MAX_KNOWN_DEVICES = 256;
}
