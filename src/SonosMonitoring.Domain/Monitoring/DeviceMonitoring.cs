using System.Text.RegularExpressions;
using SonosMonitoring.Domain.Monitoring.Disable;
using SonosMonitoring.Domain.Monitoring.Enable;
using SonosMonitoring.Domain.Monitoring.Events;
using SonosMonitoring.Platform;

namespace SonosMonitoring.Domain.Monitoring;

/// <summary>Owns the lifecycle and invariants of one device's monitoring policy.</summary>
[AggregateName(MonitoringNames.AGGREGATE)]
public sealed partial class DeviceMonitoring : AggregateRoot<DeviceMonitoring, DeviceMonitoringState, DeviceMonitoringId>
{
    /// <summary>Starts empty; historical events or a snapshot restore the policy.</summary>
    /// <param name="id">Stable stream identity.</param>
    /// <param name="changeTracker">Framework-owned transactional tracker.</param>
    public DeviceMonitoring(DeviceMonitoringId id, IChangeTracker<DeviceMonitoringId> changeTracker) : base(id, changeTracker)
    {
        this.ReplaceAggregateState(new DeviceMonitoringState(id));
    }

    /// <summary>Gets whether the policy is active.</summary>
    public bool Enabled => this.AggregateState.Enabled;
    /// <summary>Gets the bound device identity; empty means no creation fact has been applied.</summary>
    public string DeviceKey => this.AggregateState.DeviceKey;
    /// <summary>Gets the requested polling interval.</summary>
    public int IntervalSeconds => this.AggregateState.IntervalSeconds;

    /// <summary>Validates the single authoritative device-key and interval policy.</summary>
    /// <param name="deviceKey">The stable UDN without the uuid prefix.</param>
    /// <param name="intervalSeconds">The desired interval.</param>
    public static IResult ValidatePolicy(string deviceKey, int intervalSeconds)
    {
        if (string.IsNullOrEmpty(deviceKey) || deviceKey.Length > 128 ||
            Regex.IsMatch(deviceKey, @"\ARINCON_[A-Z0-9_]+\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) == false)
        {
            return new Result(new DomainError("monitoring.device.invalid", "A canonical Sonos device key is required.", DomainErrorCategory.Validation));
        }
        if (intervalSeconds is < 5 or > 300)
        {
            return new Result(new DomainError("monitoring.interval.invalid", "The interval must be between 5 and 300 seconds.", DomainErrorCategory.Validation));
        }
        return new Result();
    }

    /// <summary>Prevents silently moving a policy or treating a different intention as an edit.</summary>
    /// <param name="deviceKey">The requested device binding.</param>
    internal IResult ValidateEnable(string deviceKey)
    {
        if (this.DeviceKey.Length > 0 && this.DeviceKey != deviceKey)
        {
            return Conflict("monitoring.device.mismatch", "A policy cannot be moved to another device.");
        }
        return this.Enabled ? Conflict("monitoring.already_enabled", "Monitoring is already enabled.") : new Result();
    }

    /// <summary>Allows deactivation only for an active, existing policy.</summary>
    internal IResult ValidateDisable() => this.Enabled
        ? new Result()
        : Conflict("monitoring.not_enabled", "Monitoring is not enabled.");

    /// <summary>Records first activation or reactivation with complete resulting values.</summary>
    /// <param name="command">The validated enable intention.</param>
    internal IAggregateModificationResult Enable(EnableMonitoring command)
    {
        if (this.DeviceKey.Length == 0)
        {
            return this.Emit(command.RequestId, new DeviceMonitoringRegistered
            {
                AggregateId = this.Id,
                DeviceKey = command.DeviceKey,
                Enabled = true,
                IntervalSeconds = command.IntervalSeconds,
            });
        }
        return this.Emit(command.RequestId, new DeviceMonitoringEnabled
        {
            AggregateId = this.Id,
            DeviceKey = this.DeviceKey,
            Enabled = true,
            IntervalSeconds = command.IntervalSeconds,
        });
    }

    /// <summary>Records a reversible deactivation, preserving the device binding and interval.</summary>
    /// <param name="command">The validated disable intention.</param>
    internal IAggregateModificationResult Disable(DisableMonitoring command) => this.Emit(command.RequestId, new DeviceMonitoringDisabled
    {
        AggregateId = this.Id,
        DeviceKey = this.DeviceKey,
        Enabled = false,
        IntervalSeconds = this.IntervalSeconds,
    });

    /// <summary>Restores the first policy fact.</summary>
    /// <param name="e">The persisted creation event.</param>
    private void Apply(DeviceMonitoringRegistered e) => this.ReplaceAggregateState(new DeviceMonitoringState(this.Id, e.DeviceKey, e.Enabled, e.IntervalSeconds));
    /// <summary>Restores a later activation without recomputing historical decisions.</summary>
    /// <param name="e">The persisted activation event.</param>
    private void Apply(DeviceMonitoringEnabled e) => this.ReplaceAggregateState(new DeviceMonitoringState(this.Id, e.DeviceKey, e.Enabled, e.IntervalSeconds));
    /// <summary>Restores the deactivated policy.</summary>
    /// <param name="e">The persisted deactivation event.</param>
    private void Apply(DeviceMonitoringDisabled e) => this.ReplaceAggregateState(new DeviceMonitoringState(this.Id, e.DeviceKey, e.Enabled, e.IntervalSeconds));
    /// <summary>Builds a stable domain conflict.</summary>
    /// <param name="code">Machine-readable rejection code.</param>
    /// <param name="message">Safe diagnostic description.</param>
    private static IResult Conflict(string code, string message) => new Result(new DomainError(code, message, DomainErrorCategory.Conflict));
}
