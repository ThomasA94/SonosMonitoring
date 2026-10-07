using Devbone.Frameworks.Ddd.Core;
using Devbone.Frameworks.Ddd.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SonosMonitoring.Application.Monitoring;
using SonosMonitoring.Domain.Monitoring.Disable;
using SonosMonitoring.Domain.Monitoring.Enable;

namespace SonosMonitoring.Unit.Tests.Monitoring;

/// <summary>Protects the edit token from moving ahead of the policy state displayed to the user.</summary>
public sealed class MonitoringTokenReaderTests
{
    /// <summary>A delayed projection receives its own persisted token rather than the latest stream token.</summary>
    [Fact]
    public async Task Token_matches_the_displayed_sequence_even_when_the_stream_has_advanced()
    {
        const string DEVICE = "RINCON_TOKEN_TEST";
        var repository = new InMemoryEventRepository();
        var executor = new MonitoringExecutor(repository, repository, repository, repository, new InMemoryLockRepository(), NullLogger<MonitoringExecutor>.Instance);
        var id = MonitoringIdentity.ForDevice(DEVICE);
        var enabled = await executor.Handle([new EnableMonitoring(id, RequestId.New, DEVICE, 15)]);
        Assert.True(enabled.Success);
        var disabled = await executor.Handle([new DisableMonitoring(id, RequestId.New, enabled.CommitReceipt!.AggregateConcurrencyToken)]);
        Assert.True(disabled.Success);
        var reader = new MonitoringTokenReader(repository);
        Assert.Equal(enabled.CommitReceipt.AggregateConcurrencyToken.ToString(), reader.Read(DEVICE, 1));
        Assert.Equal(disabled.CommitReceipt!.AggregateConcurrencyToken.ToString(), reader.Read(DEVICE, 2));
        Assert.Null(reader.Read(DEVICE, 3));
        Assert.Null(reader.Read("RINCON_MISSING", 1));
    }
}
