namespace SonosMonitoring.Infrastructure.History;

/// <summary>Bounds a single collector's durable, sampled observation journal.</summary>
public sealed class ObservationHistoryOptions
{
    /// <summary>Gets or sets explicit journal enablement.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the PostgreSQL connection string, supplied externally and never logged.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Gets or sets the retention age; this is independent of numeric metric retention.</summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>Gets or sets the hard journal row bound; the first reached retention bound wins.</summary>
    public int MaxEntries { get; set; } = 20000;

    /// <summary>Gets or sets sampling frequency; faster transitions may be coalesced explicitly.</summary>
    public TimeSpan SampleInterval { get; set; } = TimeSpan.FromSeconds(2);
}
