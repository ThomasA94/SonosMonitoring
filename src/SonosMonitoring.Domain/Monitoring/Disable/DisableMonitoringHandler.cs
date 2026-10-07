namespace SonosMonitoring.Domain.Monitoring.Disable;

/// <summary>Dispatches disable intentions to the aggregate's single policy implementation.</summary>
public sealed class DisableMonitoringHandler : IAggregateCommandHandler<DeviceMonitoring, DeviceMonitoringId, DisableMonitoring>
{
    /// <summary>Rejects malformed inputs before acquiring a stream lock.</summary>
    /// <param name="command">The policy action.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IResult> ValidateInputAsync(DisableMonitoring command, CancellationToken ct) =>
        ValueTask.FromResult<IResult>(new Result());

    /// <summary>Checks the intention against the rehydrated authoritative state.</summary>
    /// <param name="aggregate">The current aggregate.</param>
    /// <param name="command">The policy action.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IResult> ValidateAggregateAsync(DeviceMonitoring aggregate, DisableMonitoring command, CancellationToken ct) =>
        ValueTask.FromResult(aggregate.ValidateDisable());

    /// <summary>Emits the validated policy fact without performing network or database I/O.</summary>
    /// <param name="aggregate">The current aggregate.</param>
    /// <param name="validatedCommand">The successfully validated action.</param>
    /// <param name="ct">Request cancellation.</param>
    public ValueTask<IAggregateModificationResult> ExecuteAsync(DeviceMonitoring aggregate, ValidatedCommand<DisableMonitoring> validatedCommand, CancellationToken ct) =>
        ValueTask.FromResult(aggregate.Disable(validatedCommand.Command));
}
