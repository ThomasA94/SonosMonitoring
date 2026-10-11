namespace SonosMonitoring.Infrastructure.MediaControl;

/// <summary>Bounds technical execution resources; permission is separately persisted and starts disabled.</summary>
public sealed class MediaControlOptions
{
    /// <summary>Gets or sets whether this deployment provides the media executor.</summary>
    public bool Enabled { get; set; }
    /// <summary>Gets or sets the separate coordination connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;
    /// <summary>Gets or sets the maximum pending operation count.</summary>
    public int MaxPending { get; set; } = 128;
    /// <summary>Gets or sets the dispatch admission lifetime.</summary>
    public int LifetimeSeconds { get; set; } = 30;
    /// <summary>Gets or sets the entire device execution deadline.</summary>
    public int ExecutionSeconds { get; set; } = 30;
    /// <summary>Gets or sets the maximum number of disjoint operations executed at once.</summary>
    public int MaxConcurrentExecutions { get; set; } = 4;
}
