using Devbone.Frameworks.Ddd.Results;
using Devbone.Frameworks.Ddd.Commands;
using Devbone.Frameworks.Ddd.Core;
using Devbone.Frameworks.Ddd.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SonosMonitoring.Application.Monitoring;
using SonosMonitoring.Domain.Monitoring;
using SonosMonitoring.Domain.Monitoring.Disable;
using SonosMonitoring.Domain.Monitoring.Enable;

namespace SonosMonitoring.Unit.Tests.Monitoring;

/// <summary>Verifies request deduplication and optimistic concurrency through the application executor.</summary>
public sealed class MonitoringExecutorTests
{
    private const string DEVICE_KEY = "RINCON_123456789ABC";

    /// <summary>Creates the executor with the framework's thread-safe in-memory persistence ports.</summary>
    /// <returns>The executor and its event repository for assertions.</returns>
    private static (MonitoringExecutor Executor, InMemoryEventRepository Repository) CreateExecutor()
    {
        InMemoryEventRepository repository = new();
        InMemoryLockRepository locks = new();
        MonitoringExecutor executor = new(
            repository,
            repository,
            repository,
            repository,
            locks,
            NullLogger<MonitoringExecutor>.Instance);
        return (executor, repository);
    }

    /// <summary>Confirms a repeated request returns the receipt from its original commit.</summary>
    [Fact]
    public async Task Repeated_request_is_idempotent_and_returns_the_original_commit_receipt()
    {
        (MonitoringExecutor executor, InMemoryEventRepository repository) = CreateExecutor();
        DeviceMonitoringId id = DeviceMonitoringId.New;
        IRequestId requestId = RequestId.New;
        EnableMonitoring command = new(id, requestId, DEVICE_KEY, 30);
        Command<DeviceMonitoringId>[] commands = [command];

        IAggregateModificationResult original = await executor.Handle(commands);
        IAggregateModificationResult retry = await executor.Handle(commands);

        original.ShouldSucceed();
        retry.ShouldSucceed();
        Assert.NotNull(original.CommitReceipt);
        Assert.NotNull(retry.CommitReceipt);
        Assert.False(original.CommitReceipt!.AlreadyCommitted);
        Assert.True(retry.CommitReceipt!.AlreadyCommitted);
        Assert.Equal(original.CommitReceipt.EventIds, retry.CommitReceipt.EventIds);
        Assert.Single(repository.LoadAggregate(id));
    }

    /// <summary>Rejects a command whose expected token does not match the latest committed stream.</summary>
    [Fact]
    public async Task Command_with_a_stale_concurrency_token_is_rejected_before_domain_execution()
    {
        (MonitoringExecutor executor, InMemoryEventRepository repository) = CreateExecutor();
        DeviceMonitoringId id = DeviceMonitoringId.New;
        IAggregateModificationResult created = await executor.Handle(
            new Command<DeviceMonitoringId>[]
            {
                new EnableMonitoring(id, RequestId.New, DEVICE_KEY, 30),
            });
        IAggregateModificationResult staleUpdate = await executor.Handle(
            new Command<DeviceMonitoringId>[]
            {
                new DisableMonitoring(id, RequestId.New, AggregateConcurrencyToken.Create()),
            });

        created.ShouldSucceed();
        staleUpdate.ShouldFailWith("ddd.concurrency.conflict");
        Assert.Single(repository.LoadAggregate(id));
    }
}
