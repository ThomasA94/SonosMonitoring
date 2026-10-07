namespace SonosMonitoring.Domain.MediaControl.Requests;

/// <summary>Routes the accept-risk decision through the operation aggregate.</summary>
public sealed class AcceptUncertainControlRiskHandler : IAggregateCommandHandler<MediaControlOperation, MediaOperationId, AcceptUncertainControlRisk>
{
    /// <summary>Validates the decision's structured input.</summary>
    /// <param name="command">Installation decision.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IResult> ValidateInputAsync(AcceptUncertainControlRisk command, CancellationToken ct) =>
        ValueTask.FromResult(MediaControlOperation.ValidateRequest(command));

    /// <summary>Rejects an already used operation stream.</summary>
    /// <param name="aggregate">Rehydrated operation.</param>
    /// <param name="command">Installation decision.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IResult> ValidateAggregateAsync(MediaControlOperation aggregate, AcceptUncertainControlRisk command, CancellationToken ct) =>
        ValueTask.FromResult(aggregate.ValidateNew());

    /// <summary>Records the decision intent for later coordinated execution.</summary>
    /// <param name="aggregate">Rehydrated operation.</param>
    /// <param name="validatedCommand">Validated decision.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IAggregateModificationResult> ExecuteAsync(MediaControlOperation aggregate,
        ValidatedCommand<AcceptUncertainControlRisk> validatedCommand, CancellationToken ct) =>
        ValueTask.FromResult(aggregate.Request(validatedCommand.Command));
}
