namespace SonosMonitoring.Domain.MediaControl;

/// <summary>Records an execution result separately from the user's requested fact.</summary>
public sealed class CompleteMediaResult : Command<MediaOperationId>
{
    /// <summary>Creates a token-bound result transition.</summary>
    /// <param name="id">Operation stream identity.</param>
    /// <param name="requestId">Stable transition request identity.</param>
    /// <param name="outcome">Result classification.</param>
    /// <param name="resultCode">Safe diagnostic code.</param>
    /// <param name="expected">Last confirmed stream token.</param>
    public CompleteMediaResult(MediaOperationId id, IRequestId requestId, string outcome,
        string? resultCode, AggregateConcurrencyToken expected) : base(1, id, requestId, expected)
    {
        this.Outcome = outcome;
        this.ResultCode = resultCode ?? string.Empty;
    }

    /// <summary>Gets the persisted result classification.</summary>
    public string Outcome { get; }
    /// <summary>Gets a safe machine-readable diagnostic code.</summary>
    public string ResultCode { get; }
}
