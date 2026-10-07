namespace SonosMonitoring.Domain.MediaControl.Requests;

/// <summary>Routes the start-favorite intention through the operation aggregate.</summary>
public sealed class StartFavoriteHandler : IAggregateCommandHandler<MediaControlOperation, MediaOperationId, StartFavorite>
{
    /// <summary>Validates the named action's structured input before loading the stream.</summary>
    /// <param name="command">User intention.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IResult> ValidateInputAsync(StartFavorite command, CancellationToken ct) =>
        ValueTask.FromResult(MediaControlOperation.ValidateRequest(command));

    /// <summary>Rejects reuse of an existing operation stream.</summary>
    /// <param name="aggregate">Rehydrated operation.</param>
    /// <param name="command">User intention.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IResult> ValidateAggregateAsync(MediaControlOperation aggregate, StartFavorite command, CancellationToken ct) =>
        ValueTask.FromResult(aggregate.ValidateNew());

    /// <summary>Records the user intention without a device call.</summary>
    /// <param name="aggregate">Rehydrated operation.</param>
    /// <param name="validatedCommand">Validated named intention.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IAggregateModificationResult> ExecuteAsync(MediaControlOperation aggregate,
        ValidatedCommand<StartFavorite> validatedCommand, CancellationToken ct) =>
        ValueTask.FromResult(aggregate.Request(validatedCommand.Command));
}
