namespace SonosMonitoring.Domain.MediaControl;

/// <summary>Commits one dispatch marker before any corresponding device write.</summary>
public sealed class StartMediaDispatchHandler : IAggregateCommandHandler<MediaControlOperation, MediaOperationId, StartMediaDispatch>
{
    /// <summary>Checks the writing step index.</summary>
    /// <param name="command">Dispatch transition.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IResult> ValidateInputAsync(StartMediaDispatch command, CancellationToken ct) =>
        ValueTask.FromResult<IResult>(command.Step >= 0 ? new Result() :
            new Result(new DomainError("media.dispatch.invalid", "A nonnegative step is required.", DomainErrorCategory.Validation)));

    /// <summary>Checks durable phase and deadline against current state.</summary>
    /// <param name="aggregate">Rehydrated operation.</param>
    /// <param name="command">Dispatch transition.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IResult> ValidateAggregateAsync(MediaControlOperation aggregate, StartMediaDispatch command,
        CancellationToken ct) => ValueTask.FromResult(aggregate.ValidateStart(command));

    /// <summary>Persists the dispatch beginning.</summary>
    /// <param name="aggregate">Rehydrated operation.</param>
    /// <param name="validatedCommand">Validated dispatch transition.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IAggregateModificationResult> ExecuteAsync(MediaControlOperation aggregate,
        ValidatedCommand<StartMediaDispatch> validatedCommand, CancellationToken ct) =>
        ValueTask.FromResult(aggregate.Start(validatedCommand.Command));
}
