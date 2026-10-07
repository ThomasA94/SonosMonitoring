namespace SonosMonitoring.Domain.MediaControl;

/// <summary>Durably marks a writing step before an external device call.</summary>
public sealed class StartMediaDispatch : Command<MediaOperationId>
{
    /// <summary>Creates a token-bound dispatch transition.</summary>
    /// <param name="id">Operation stream identity.</param>
    /// <param name="requestId">Stable transition request identity.</param>
    /// <param name="startedAtUtc">Time checked against the operation deadline.</param>
    /// <param name="step">Zero-based writing step.</param>
    /// <param name="expected">Last confirmed stream token.</param>
    public StartMediaDispatch(MediaOperationId id, IRequestId requestId, DateTimeOffset startedAtUtc,
        int step, AggregateConcurrencyToken expected) : base(1, id, requestId, expected)
    {
        this.StartedAtUtc = startedAtUtc;
        this.Step = step;
    }

    /// <summary>Gets the attempted dispatch start time.</summary>
    public DateTimeOffset StartedAtUtc { get; }
    /// <summary>Gets the zero-based writing step.</summary>
    public int Step { get; }
}
