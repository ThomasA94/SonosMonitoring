using System.Text.Json;
using SonosMonitoring.Application.MediaControl;
using SonosMonitoring.Infrastructure.MediaControl;

namespace SonosMonitoring.Integration.Tests.MediaControl;

/// <summary>Exercises durable target order, generation fences, risk release and executor ownership on real PostgreSQL.</summary>
public sealed class MediaCoordinationStoreTests : IClassFixture<MediaCoordinationFixture>
{
    private readonly MediaCoordinationFixture _fixture;

    /// <summary>Receives an isolated database with the explicitly installed media schema.</summary>
    /// <param name="fixture">Fixture-owned infrastructure.</param>
    public MediaCoordinationStoreTests(MediaCoordinationFixture fixture) => this._fixture = fixture;

    /// <summary>Constructs a separate store instance against the same durable database.</summary>
    private PostgresMediaCoordinationStore Store() => new(new MediaControlOptions
    {
        Enabled = true,
        ConnectionString = this._fixture.ConnectionString,
        MaxPending = 128,
    });

    /// <summary>Admits one action using the currently persisted permission generation.</summary>
    /// <param name="store">Coordinator under test.</param>
    /// <param name="generation">Grant generation bound to the action.</param>
    /// <param name="targets">All player identities affected by this action.</param>
    /// <param name="id">Optional fixed id for retry cases.</param>
    /// <param name="fingerprint">Optional fixed fingerprint for retry cases.</param>
    private static async Task<CoordinatedMediaOperation> AdmitAsync(PostgresMediaCoordinationStore store, string generation,
        string[] targets, Guid? id = null, string? fingerprint = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var epochs = await store.EpochsAsync(targets, CancellationToken.None);
        CoordinatedMediaOperation action = new(id ?? Guid.NewGuid(), 0, fingerprint ?? new string('a', 64),
            generation, targets, JsonSerializer.Serialize(new { TargetEpochs = epochs }), now, now.AddMinutes(2));
        return await store.AdmitAsync(action, CancellationToken.None);
    }

    /// <summary>Applies a new operator permission decision against its captured current generation.</summary>
    /// <param name="store">Coordinator under test.</param>
    /// <param name="enabled">Requested permission state.</param>
    private static async Task<MediaGrantStatus> GrantAsync(PostgresMediaCoordinationStore store, bool enabled)
    {
        string generation = (await store.StatusAsync(CancellationToken.None)).Generation;
        return await store.SetGrantAsync(enabled, Guid.NewGuid(), generation, CancellationToken.None);
    }

    /// <summary>Confirms overlapping operations retain admission order even if release is attempted in reverse order.</summary>
    [Fact]
    public async Task Overlapping_targets_cannot_release_in_reverse_order()
    {
        var store = this.Store();
        var grant = await GrantAsync(store, true);
        string target = "RINCON_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var first = await AdmitAsync(store, grant.Generation, [target, "RINCON_GROUP_MEMBER"]);
        var second = await AdmitAsync(store, grant.Generation, [target]);
        Assert.True(first.Position < second.Position);
        await using var owner = await store.AcquireExecutorAsync(CancellationToken.None);
        Assert.NotNull(owner);

        Assert.False(await store.ReleaseAsync(second.OperationId, 0, CancellationToken.None));
        Assert.True(await store.ReleaseAsync(first.OperationId, 0, CancellationToken.None));
        await store.FinishAsync(first.OperationId, "completed", null, CancellationToken.None);
        Assert.True(await store.ReleaseAsync(second.OperationId, 0, CancellationToken.None));
    }

    /// <summary>Reopening the coordinator preserves an unknown fence through grant revocation and renewal.</summary>
    [Fact]
    public async Task Unknown_fence_survives_restart_and_grant_changes()
    {
        var firstStore = this.Store();
        var grant = await GrantAsync(firstStore, true);
        string target = "RINCON_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var uncertain = await AdmitAsync(firstStore, grant.Generation, [target]);
        await using (var owner = await firstStore.AcquireExecutorAsync(CancellationToken.None))
        {
            Assert.NotNull(owner);
            Assert.True(await firstStore.ReleaseAsync(uncertain.OperationId, 0, CancellationToken.None));
            await firstStore.RecoverAsync(CancellationToken.None);
        }

        var restarted = this.Store();
        var disabled = await GrantAsync(restarted, false);
        Assert.False(disabled.Enabled);
        var enabled = await GrantAsync(restarted, true);
        Assert.Contains(enabled.BlockedOperations, operation => operation.OperationId == uncertain.OperationId);
        var error = await Assert.ThrowsAsync<MediaCoordinationException>(() =>
            AdmitAsync(restarted, enabled.Generation, [target]));
        Assert.Equal("media.target.blocked", error.Code);
    }

    /// <summary>An exact risk decision releases uncertainty while permanently invalidating older queued work.</summary>
    [Fact]
    public async Task Exact_risk_acceptance_invalidates_queued_work()
    {
        var store = this.Store();
        var grant = await GrantAsync(store, true);
        string target = "RINCON_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var uncertain = await AdmitAsync(store, grant.Generation, [target]);
        var queued = await AdmitAsync(store, grant.Generation, [target]);
        await store.FinishAsync(uncertain.OperationId, "outcome_unknown", "media.outcome.unknown", CancellationToken.None);

        var wrong = await Assert.ThrowsAsync<MediaCoordinationException>(() =>
            store.AcceptRiskAsync([queued.OperationId], Guid.NewGuid(), CancellationToken.None));
        Assert.Equal("media.risk.not_uncertain", wrong.Code);
        var status = await store.AcceptRiskAsync([uncertain.OperationId], Guid.NewGuid(), CancellationToken.None);
        Assert.DoesNotContain(status.BlockedOperations, operation => operation.OperationId == uncertain.OperationId);
        Assert.Equal("revoked", (await store.FindAsync(queued.OperationId, CancellationToken.None))!.Status);
        await using var owner = await store.AcquireExecutorAsync(CancellationToken.None);
        Assert.NotNull(owner);
        Assert.False(await store.ReleaseAsync(queued.OperationId, 0, CancellationToken.None));
        var next = await AdmitAsync(store, status.Generation, [target]);
        Assert.Equal("accepted", next.Status);
    }

    /// <summary>Revocation permanently fences a waiting operation even after permission is enabled again.</summary>
    [Fact]
    public async Task Old_generation_never_revives_after_reenable()
    {
        var store = this.Store();
        var original = await GrantAsync(store, true);
        string target = "RINCON_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var waiting = await AdmitAsync(store, original.Generation, [target]);
        await GrantAsync(store, false);
        var renewed = await GrantAsync(store, true);
        Assert.NotEqual(original.Generation, renewed.Generation);
        await using var owner = await store.AcquireExecutorAsync(CancellationToken.None);
        Assert.NotNull(owner);

        Assert.False(await store.ReleaseAsync(waiting.OperationId, 0, CancellationToken.None));
        Assert.Equal("revoked", (await store.FindAsync(waiting.OperationId, CancellationToken.None))!.Status);
        var stale = await Assert.ThrowsAsync<MediaCoordinationException>(() =>
            AdmitAsync(store, original.Generation, [target]));
        Assert.Equal("media.grant.revoked", stale.Code);
    }

    /// <summary>Exact retries preserve admission while altered reuse of an id is rejected.</summary>
    [Fact]
    public async Task Operation_identity_rejects_a_different_fingerprint()
    {
        var store = this.Store();
        var grant = await GrantAsync(store, true);
        string target = "RINCON_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        Guid id = Guid.NewGuid();
        var original = await AdmitAsync(store, grant.Generation, [target], id, new string('a', 64));
        var retry = await AdmitAsync(store, grant.Generation, [target], id, new string('a', 64));
        Assert.Equal(original.Position, retry.Position);
        var conflict = await Assert.ThrowsAsync<MediaCoordinationException>(() =>
            AdmitAsync(store, grant.Generation, [target], id, new string('b', 64)));
        Assert.Equal("media.idempotency.conflict", conflict.Code);
    }

    /// <summary>One executor owns dispatch and each writing step receives at most one release.</summary>
    [Fact]
    public async Task Executor_is_exclusive_and_step_release_is_once_only()
    {
        var first = this.Store();
        var second = this.Store();
        var grant = await GrantAsync(first, true);
        string target = "RINCON_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var operation = await AdmitAsync(first, grant.Generation, [target]);
        await using (var owner = await first.AcquireExecutorAsync(CancellationToken.None))
        {
            Assert.NotNull(owner);
            Assert.True(await first.OwnsExecutorAsync(CancellationToken.None));
            Assert.Null(await second.AcquireExecutorAsync(CancellationToken.None));
            Assert.False(await second.ReleaseAsync(operation.OperationId, 0, CancellationToken.None));
            Assert.True(await first.ReleaseAsync(operation.OperationId, 0, CancellationToken.None));
            Assert.False(await first.ReleaseAsync(operation.OperationId, 0, CancellationToken.None));
        }
        await using var successor = await second.AcquireExecutorAsync(CancellationToken.None);
        Assert.NotNull(successor);
        Assert.True(await second.OwnsExecutorAsync(CancellationToken.None));
    }

    /// <summary>A pre-risk domain intent without an admission cannot be admitted after its target epoch advances.</summary>
    [Fact]
    public async Task Risk_acceptance_rejects_old_unadmitted_intent_on_retry()
    {
        var store = this.Store();
        var grant = await GrantAsync(store, true);
        string target = "RINCON_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var uncertain = await AdmitAsync(store, grant.Generation, [target]);
        await store.FinishAsync(uncertain.OperationId, "outcome_unknown", "media.outcome.unknown", CancellationToken.None);
        DateTimeOffset accepted = DateTimeOffset.UtcNow;
        Guid orphanId = Guid.NewGuid();
        string originalPayload = JsonSerializer.Serialize(new
        {
            TargetEpochs = await store.EpochsAsync([target], CancellationToken.None),
        });
        CoordinatedMediaOperation orphan = new(orphanId, 0, new string('b', 64), grant.Generation,
            [target], originalPayload, accepted, accepted.AddMinutes(2));
        await store.AcceptRiskAsync([uncertain.OperationId], Guid.NewGuid(), CancellationToken.None);

        var rejected = await Assert.ThrowsAsync<MediaCoordinationException>(() =>
            store.AdmitAsync(orphan, CancellationToken.None));
        Assert.Equal("media.risk.old_work_invalidated", rejected.Code);
        Assert.Null(await store.FindAsync(orphanId, CancellationToken.None));
        var fresh = await AdmitAsync(store, grant.Generation, [target]);
        Assert.Equal("accepted", fresh.Status);
    }

    /// <summary>An older delayed enable cannot undo a later disable decision.</summary>
    [Fact]
    public async Task Stale_enable_decision_cannot_reverse_a_later_disable()
    {
        var store = this.Store();
        var initial = await store.StatusAsync(CancellationToken.None);
        Guid delayedEnableId = Guid.NewGuid();
        var disabled = await store.SetGrantAsync(false, Guid.NewGuid(), initial.Generation, CancellationToken.None);

        var stale = await Assert.ThrowsAsync<MediaCoordinationException>(() =>
            store.SetGrantAsync(true, delayedEnableId, initial.Generation, CancellationToken.None));
        Assert.Equal("media.grant.stale_decision", stale.Code);
        var after = await store.StatusAsync(CancellationToken.None);
        Assert.False(after.Enabled);
        Assert.Equal(disabled.Generation, after.Generation);
    }
}
