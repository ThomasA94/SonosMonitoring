namespace SonosMonitoring.Domain.MediaControl;

/// <summary>Records an execution result through the authoritative operation stream.</summary>
public sealed class CompleteMediaResultHandler : IAggregateCommandHandler<MediaControlOperation, MediaOperationId, CompleteMediaResult>
{
    /// <summary>Defers phase-sensitive validation to the rehydrated aggregate.</summary>
    /// <param name="command">Result transition.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IResult> ValidateInputAsync(CompleteMediaResult command, CancellationToken ct) =>
        ValueTask.FromResult<IResult>(new Result());

    /// <summary>Checks that the result matches the current phase.</summary>
    /// <param name="aggregate">Rehydrated operation.</param>
    /// <param name="command">Result transition.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IResult> ValidateAggregateAsync(MediaControlOperation aggregate, CompleteMediaResult command,
        CancellationToken ct) => ValueTask.FromResult(aggregate.ValidateComplete(command));

    /// <summary>Persists the result without performing external I/O.</summary>
    /// <param name="aggregate">Rehydrated operation.</param>
    /// <param name="validatedCommand">Validated result transition.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IAggregateModificationResult> ExecuteAsync(MediaControlOperation aggregate,
        ValidatedCommand<CompleteMediaResult> validatedCommand, CancellationToken ct) =>
        ValueTask.FromResult(aggregate.Complete(validatedCommand.Command));
}
