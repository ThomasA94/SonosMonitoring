namespace SonosMonitoring.Domain.MediaControl.Requests;

/// <summary>Routes the queue-remove intention through the operation aggregate.</summary>
public sealed class RemoveQueueItemHandler : IAggregateCommandHandler<MediaControlOperation, MediaOperationId, RemoveQueueItem>
{
    /// <summary>Validates the named action's structured input before loading the stream.</summary>
    /// <param name="command">User intention.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IResult> ValidateInputAsync(RemoveQueueItem command, CancellationToken ct) =>
        ValueTask.FromResult(MediaControlOperation.ValidateRequest(command));

    /// <summary>Rejects reuse of an existing operation stream.</summary>
    /// <param name="aggregate">Rehydrated operation.</param>
    /// <param name="command">User intention.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IResult> ValidateAggregateAsync(MediaControlOperation aggregate, RemoveQueueItem command, CancellationToken ct) =>
        ValueTask.FromResult(aggregate.ValidateNew());

    /// <summary>Records the user intention without a device call.</summary>
    /// <param name="aggregate">Rehydrated operation.</param>
    /// <param name="validatedCommand">Validated named intention.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IAggregateModificationResult> ExecuteAsync(MediaControlOperation aggregate,
        ValidatedCommand<RemoveQueueItem> validatedCommand, CancellationToken ct) =>
        ValueTask.FromResult(aggregate.Request(validatedCommand.Command));
}
