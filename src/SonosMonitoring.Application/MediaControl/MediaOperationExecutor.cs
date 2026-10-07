using System.Security.Cryptography;
using System.Text;
using Devbone.Frameworks.Ddd.Aggregates;
using Devbone.Frameworks.Ddd.ChangeTracker;
using Devbone.Frameworks.Ddd.Commands;
using Devbone.Frameworks.Ddd.Core;
using Devbone.Frameworks.Ddd.Repositories;
using Devbone.Frameworks.Ddd.Results;
using Microsoft.Extensions.Logging;
using SonosMonitoring.Domain.MediaControl;
using SonosMonitoring.Domain.MediaControl.Requests;
using SonosMonitoring.Platform;

namespace SonosMonitoring.Application.MediaControl;

/// <summary>Commits media intentions and result transitions through the framework event store.</summary>
public sealed class MediaOperationExecutor : CommandExecutor<MediaControlOperation, MediaOperationId>
{
    private readonly IEventStreamWriter _writer;
    private readonly IUniqueValueRepository _uniques;
    private readonly IIncrementValueRepository _increments;

    /// <summary>Receives the existing persistence and lock ports.</summary>
    /// <param name="reader">Historical event reader.</param>
    /// <param name="writer">Transactional event and outbox writer.</param>
    /// <param name="uniques">Framework unique reservation port.</param>
    /// <param name="increments">Framework increment port.</param>
    /// <param name="locks">Framework aggregate lock port.</param>
    /// <param name="logger">Structured executor logger.</param>
    public MediaOperationExecutor(IEventStreamReader reader, IEventStreamWriter writer,
        IUniqueValueRepository uniques, IIncrementValueRepository increments,
        IAggregateLockPort locks, ILogger<MediaOperationExecutor> logger)
        : base(1, new AggregateName(MediaControlNames.AGGREGATE), reader, locks, logger)
    {
        this._writer = writer;
        this._uniques = uniques;
        this._increments = increments;
    }

    /// <summary>Commits a single transition under its operation stream lock.</summary>
    /// <param name="commands">One or more commands for the same operation.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    public override Task<IAggregateModificationResult> Handle(IEnumerable<Command<MediaOperationId>> commands,
        CancellationToken cancellationToken = default) =>
        this.HandleLockedAsync(commands, id => new MediaControlOperation(id,
            ChangeTrackerFactory.CreateWithContext<MediaOperationId>(id, this._writer, this._uniques,
                this._increments, new AggregateCommitContext { TenantId = MediaControlNames.TENANT })), cancellationToken);

    /// <summary>Commits one named user or operator intention.</summary>
    /// <param name="operationId">Stable identity derived from the Idempotency-Key.</param>
    /// <param name="action">Named media intention.</param>
    /// <param name="deviceKey">Stable selected device or installation.</param>
    /// <param name="payloadJson">Sanitized immutable execution context.</param>
    /// <param name="fingerprint">Hash of complete request inputs and context.</param>
    /// <param name="generation">Captured grant generation.</param>
    /// <param name="acceptedAtUtc">Acceptance time.</param>
    /// <param name="expiresAtUtc">Dispatch deadline.</param>
    /// <param name="inputs">Action-specific structured values.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    public Task<IAggregateModificationResult> RequestAsync(Guid operationId, string action, string deviceKey,
        string payloadJson, string fingerprint, string generation, DateTimeOffset acceptedAtUtc,
        DateTimeOffset expiresAtUtc, MediaIntentInputs inputs, CancellationToken cancellationToken = default)
    {
        MediaOperationId id = MediaOperationId.With(operationId);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{operationId:N}/{fingerprint}"));
        IRequestId requestId = RequestId.With(new Guid(digest.AsSpan(0, 16)));
        Command<MediaOperationId> command = action switch
        {
            "resume" => new ResumeGroupPlayback(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "pause" => new PauseGroupPlayback(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "stop" => new StopGroupPlayback(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "volume" => new ChoosePlayerVolume(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "mute" => new MutePlayer(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "unmute" => new UnmutePlayer(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "next" => new NextTrack(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "previous" => new PreviousTrack(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "seek" => new SeekPlayback(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "mode" => new ChoosePlaybackMode(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "crossfade" => new ChooseCrossfade(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "start-favorite" => new StartFavorite(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "start-stream" => new StartStream(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "queue-play" => new PlayQueueItem(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "queue-append" => new AppendQueueItem(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "queue-remove" => new RemoveQueueItem(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "queue-reorder" => new ReorderQueueItem(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "queue-clear" => new ClearQueue(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "group-volume" => new ChooseGroupVolume(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "group-mute" => new MuteGroup(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "group-unmute" => new UnmuteGroup(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "join" => new JoinGroup(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "leave" => new LeaveGroup(id, requestId, deviceKey, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "enable-control" => new EnableMediaControl(id, requestId, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "disable-control" => new DisableMediaControl(id, requestId, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            "accept-risk" => new AcceptUncertainControlRisk(id, requestId, payloadJson, fingerprint, generation, acceptedAtUtc, expiresAtUtc, inputs),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported media action."),
        };
        return this.Handle([command], cancellationToken);
    }

    /// <summary>Commits one irreversible dispatch marker using the last confirmed token.</summary>
    /// <param name="operationId">Operation identity.</param>
    /// <param name="expectedToken">Last confirmed opaque concurrency token.</param>
    /// <param name="requestId">Stable transition request identity.</param>
    /// <param name="step">Zero-based writing step.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    public Task<IAggregateModificationResult> StartAsync(Guid operationId, string expectedToken, Guid requestId,
        int step = 0, CancellationToken cancellationToken = default) => this.Handle(
        [new StartMediaDispatch(MediaOperationId.With(operationId), RequestId.With(requestId),
            DateTimeOffset.UtcNow, step, AggregateConcurrencyToken.Parse(expectedToken))], cancellationToken);

    /// <summary>Commits the result of a device step or installation decision.</summary>
    /// <param name="operationId">Operation identity.</param>
    /// <param name="expectedToken">Last confirmed opaque concurrency token.</param>
    /// <param name="requestId">Stable transition request identity.</param>
    /// <param name="outcome">Acknowledged, observed, rejected, expired, revoked, or unknown.</param>
    /// <param name="resultCode">Safe diagnostic code.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    public Task<IAggregateModificationResult> CompleteAsync(Guid operationId, string expectedToken, Guid requestId,
        string outcome, string? resultCode = null, CancellationToken cancellationToken = default) => this.Handle(
        [new CompleteMediaResult(MediaOperationId.With(operationId), RequestId.With(requestId),
            outcome, resultCode, AggregateConcurrencyToken.Parse(expectedToken))], cancellationToken);
}
