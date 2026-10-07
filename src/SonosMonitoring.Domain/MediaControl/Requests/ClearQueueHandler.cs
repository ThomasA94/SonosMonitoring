namespace SonosMonitoring.Domain.MediaControl.Requests;

/// <summary>Routes the queue-clear intention through the operation aggregate.</summary>
public sealed class ClearQueueHandler : IAggregateCommandHandler<MediaControlOperation, MediaOperationId, ClearQueue>
{
    /// <summary>Validates the named action's structured input before loading the stream.</summary>
    /// <param name="command">User intention.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IResult> ValidateInputAsync(ClearQueue command, CancellationToken ct) =>
        ValueTask.FromResult(MediaControlOperation.ValidateRequest(command));

    /// <summary>Rejects reuse of an existing operation stream.</summary>
    /// <param name="aggregate">Rehydrated operation.</param>
    /// <param name="command">User intention.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IResult> ValidateAggregateAsync(MediaControlOperation aggregate, ClearQueue command, CancellationToken ct) =>
        ValueTask.FromResult(aggregate.ValidateNew());

    /// <summary>Records the user intention without a device call.</summary>
    /// <param name="aggregate">Rehydrated operation.</param>
    /// <param name="validatedCommand">Validated named intention.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IAggregateModificationResult> ExecuteAsync(MediaControlOperation aggregate,
        ValidatedCommand<ClearQueue> validatedCommand, CancellationToken ct) =>
        ValueTask.FromResult(aggregate.Request(validatedCommand.Command));
}
