using System.Text.Json;
using Devbone.Frameworks.Ddd.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SonosMonitoring.Application.MediaControl;
using SonosMonitoring.Application.MediaControl.Transport;
using SonosMonitoring.Domain.MediaControl;
using SonosMonitoring.Domain.MediaControl.Requests;

namespace SonosMonitoring.Unit.Tests.MediaControl;

/// <summary>Verifies operation outcomes after multi-step device execution.</summary>
public sealed class MediaExecutionServiceTests
{
    private const string DEVICE_KEY = "RINCON_123456789ABC";

    /// <summary>Retains an explicit partial rejection only when an earlier device step was acknowledged.</summary>
    /// <param name="rejectOnAttempt">One-based device send that returns a definite rejection.</param>
    /// <param name="expectedCode">Stable result code recorded in the operation stream.</param>
    [Theory]
    [InlineData(1, "media.action_rejected")]
    [InlineData(2, "media.partial.rejected")]
    public async Task Definite_rejection_records_partial_code_only_after_an_acknowledged_step(
        int rejectOnAttempt, string expectedCode)
    {
        InMemoryEventRepository repository = new();
        MediaOperationExecutor executor = new(repository, repository, repository, repository,
            new InMemoryLockRepository(), NullLogger<MediaOperationExecutor>.Instance);
        Guid operationId = Guid.NewGuid();
        MediaTargetContext context = new(DEVICE_KEY, "household-test", DEVICE_KEY, [DEVICE_KEY], "source-test", "queue-test");
        MediaExecutionPayload payload = new("volume", DEVICE_KEY, context, new MediaWriteInputs(Value: 42));
        string payloadJson = JsonSerializer.Serialize(payload);
        DateTimeOffset acceptedAtUtc = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAtUtc = acceptedAtUtc.AddMinutes(1);
        MediaRequestService.Ensure(await executor.RequestAsync(operationId, "volume", DEVICE_KEY, payloadJson,
            "fingerprint", "generation", acceptedAtUtc, expiresAtUtc, new MediaIntentInputs(Value: 42)));
        MediaOperationReader reader = new(repository);
        TestMediaCoordinationStore store = new(new CoordinatedMediaOperation(operationId, 0, "fingerprint", "generation",
            [DEVICE_KEY], payloadJson, acceptedAtUtc, expiresAtUtc));
        TestMediaDeviceGateway devices = new(rejectOnAttempt);
        MediaExecutionService service = new(executor, reader, store, devices);

        await service.ProcessAsync(store.Operation, CancellationToken.None);

        MediaOperationView state = (await reader.ReadAsync(operationId))!;
        Assert.Equal("rejected", state.State.Status);
        Assert.Equal(expectedCode, state.State.ResultCode);
        Assert.Equal("rejected", store.Operation.Status);
        Assert.Equal(expectedCode, store.Operation.Code);
        Assert.Equal(rejectOnAttempt, devices.SendCount);
        Assert.Equal(rejectOnAttempt, store.ReleaseCount);
        if (rejectOnAttempt == 2)
        {
            Assert.Equal(1, state.State.Step);
            Assert.Equal(5L, state.EventSequence);
        }
    }

}
