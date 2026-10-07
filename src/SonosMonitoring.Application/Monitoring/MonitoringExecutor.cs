using Devbone.Frameworks.Ddd.Aggregates;
using Devbone.Frameworks.Ddd.ChangeTracker;
using Devbone.Frameworks.Ddd.Commands;
using Devbone.Frameworks.Ddd.Repositories;
using Devbone.Frameworks.Ddd.Results;
using Microsoft.Extensions.Logging;
using SonosMonitoring.Domain.Monitoring;
using SonosMonitoring.Platform;

namespace SonosMonitoring.Application.Monitoring;

/// <summary>Runs monitoring intentions through the framework's lock, rehydration and atomic commit path.</summary>
public sealed class MonitoringExecutor : CommandExecutor<DeviceMonitoring, DeviceMonitoringId>
{
    private readonly IEventStreamWriter _writer;
    private readonly IUniqueValueRepository _uniques;
    private readonly IIncrementValueRepository _increments;

    /// <summary>Receives the Direct adapter ports from the composition root.</summary>
    /// <param name="reader">Historical event and snapshot reader.</param>
    /// <param name="writer">Transactional event and outbox writer.</param>
    /// <param name="uniques">Framework reservation port.</param>
    /// <param name="increments">Framework increment port.</param>
    /// <param name="locks">Framework aggregate lock port.</param>
    /// <param name="logger">Structured executor diagnostics.</param>
    public MonitoringExecutor(IEventStreamReader reader, IEventStreamWriter writer, IUniqueValueRepository uniques,
        IIncrementValueRepository increments, IAggregateLockPort locks, ILogger<MonitoringExecutor> logger)
        : base(1, new AggregateName(MonitoringNames.AGGREGATE), reader, locks, logger)
    {
        this._writer = writer;
        this._uniques = uniques;
        this._increments = increments;
    }

    /// <summary>Commits a batch with a fixed installation tenant. No device calls occur under the lock.</summary>
    /// <param name="commands">Commands for one stream and request identity.</param>
    /// <param name="cancellationToken">Cancellation propagated to the framework.</param>
    public override Task<IAggregateModificationResult> Handle(IEnumerable<Command<DeviceMonitoringId>> commands, CancellationToken cancellationToken = default) =>
        this.HandleLockedAsync(commands, id => new DeviceMonitoring(id,
            ChangeTrackerFactory.CreateWithContext<DeviceMonitoringId>(id, this._writer, this._uniques, this._increments,
                new AggregateCommitContext { TenantId = MonitoringNames.TENANT })), cancellationToken);
}
