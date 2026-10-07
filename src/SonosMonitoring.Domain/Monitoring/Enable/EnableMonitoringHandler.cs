namespace SonosMonitoring.Domain.Monitoring.Enable;

/// <summary>Dispatches enable intentions to the aggregate's single policy implementation.</summary>
public sealed class EnableMonitoringHandler : IAggregateCommandHandler<DeviceMonitoring, DeviceMonitoringId, EnableMonitoring>
{
    /// <summary>Rejects malformed inputs before acquiring a stream lock.</summary>
    /// <param name="command">The policy action.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IResult> ValidateInputAsync(EnableMonitoring command, CancellationToken ct) =>
        ValueTask.FromResult<IResult>(DeviceMonitoring.ValidatePolicy(command.DeviceKey, command.IntervalSeconds));

    /// <summary>Checks the intention against the rehydrated authoritative state.</summary>
    /// <param name="aggregate">The current aggregate.</param>
    /// <param name="command">The policy action.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IResult> ValidateAggregateAsync(DeviceMonitoring aggregate, EnableMonitoring command, CancellationToken ct) =>
        ValueTask.FromResult(aggregate.ValidateEnable(command.DeviceKey));

    /// <summary>Emits the validated policy fact without performing network or database I/O.</summary>
    /// <param name="aggregate">The current aggregate.</param>
    /// <param name="validatedCommand">The successfully validated action.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IAggregateModificationResult> ExecuteAsync(DeviceMonitoring aggregate, ValidatedCommand<EnableMonitoring> validatedCommand, CancellationToken ct) =>
        ValueTask.FromResult(aggregate.Enable(validatedCommand.Command));
}
