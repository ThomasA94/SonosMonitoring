using SonosMonitoring.Infrastructure.Collection;
using SonosMonitoring.Infrastructure.Collection.Gena;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Checks per-lease evidence independently of network timing.</summary>
public sealed class GenaSessionTests
{
    /// <summary>Preserves an initial event racing its subscription response and coalesces later hints.</summary>
    [Fact]
    public void EarlyInitialNotificationIsConfirmedAndDuplicatesDoNotPoll()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new GenaSession("player", "192.0.2.1");
        Assert.True(session.Accept(new("uuid:one", 0), true, now));
        Assert.False(session.TakePoll(now, TimeSpan.FromSeconds(2)));
        session.Confirm(new("uuid:one", TimeSpan.FromSeconds(30)), now, 30);
        Assert.True(session.Snapshot(now, TimeSpan.FromSeconds(5)).InitialNotificationReceived);
        Assert.True(session.TakePoll(now, TimeSpan.FromSeconds(2)));
        Assert.True(session.Accept(new("uuid:one", 0), true, now));
        Assert.False(session.TakePoll(now.AddSeconds(3), TimeSpan.FromSeconds(2)));
        Assert.True(session.Accept(new("uuid:one", 1), true, now.AddSeconds(3)));
        Assert.True(session.Accept(new("uuid:one", 2), true, now.AddSeconds(3)));
        Assert.True(session.TakePoll(now.AddSeconds(3), TimeSpan.FromSeconds(2)));
        Assert.False(session.TakePoll(now.AddSeconds(6), TimeSpan.FromSeconds(2)));
    }

    /// <summary>Discards provisional evidence belonging to a different response lease.</summary>
    [Fact]
    public void WrongLeaseAndMissingInitialRemainUnconfirmed()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new GenaSession("player", "192.0.2.1");
        Assert.True(session.Accept(new("uuid:old", 0), true, now));
        session.Confirm(new("uuid:new", null), now, 30);
        Assert.False(session.Accept(new("uuid:old", 1), true, now));
        Assert.False(session.Accept(new("uuid:new", 1), true, now));
        Assert.False(session.TakePoll(now, TimeSpan.FromSeconds(2)));
        Assert.Equal("callback_unconfirmed", session.Snapshot(now.AddSeconds(3), TimeSpan.FromSeconds(2)).ErrorCode);
        Assert.True(session.InitialMissing(now.AddSeconds(5), TimeSpan.FromSeconds(2)));
        session.Confirm(new("uuid:new", null), now.AddSeconds(4), 30);
        Assert.True(session.InitialMissing(now.AddSeconds(5), TimeSpan.FromSeconds(2)));
        Assert.True(session.Accept(new("uuid:new", 0), false, now.AddSeconds(6)));
        Assert.False(session.InitialMissing(now.AddDays(1), TimeSpan.FromSeconds(2)));
    }

    /// <summary>Gaps trigger reconciliation; delayed events cannot replace current ordering evidence.</summary>
    [Fact]
    public void SequenceGapAndWrapAreHandledWithoutReplayingOldEvents()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new GenaSession("player", "192.0.2.1");
        session.Confirm(new("uuid:one", null), now, 30);
        Assert.True(session.Accept(new("uuid:one", 0), false, now));
        Assert.True(session.Accept(new("uuid:one", 2), false, now));
        Assert.Equal("sequence_gap", session.Snapshot(now, TimeSpan.FromSeconds(2)).ErrorCode);
        Assert.True(session.TakePoll(now, TimeSpan.FromSeconds(2)));
        Assert.True(session.Accept(new("uuid:one", 1), true, now));
        Assert.False(session.TakePoll(now.AddSeconds(3), TimeSpan.FromSeconds(2)));
        Assert.True(session.Accept(new("uuid:one", 2147483648), false, now));
        Assert.True(session.Accept(new("uuid:one", uint.MaxValue), false, now));
        Assert.True(session.Accept(new("uuid:one", 1), false, now));
        Assert.Null(session.Snapshot(now, TimeSpan.FromSeconds(2)).ErrorCode);
    }

    /// <summary>Actual granted duration and rejection govern expiry instead of the requested duration.</summary>
    [Fact]
    public void GrantedLeaseRenewsEarlyAndRejectedLeaseExpires()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new GenaSession("player", "192.0.2.1");
        session.BeginAttempt(now);
        session.Confirm(new("uuid:one", TimeSpan.FromSeconds(6)), now, 1800);
        Assert.False(session.Due(now.AddSeconds(2)));
        Assert.True(session.Due(now.AddSeconds(3)));
        session.Failed(now.AddSeconds(3));
        Assert.True(session.Snapshot(now.AddSeconds(4), TimeSpan.FromSeconds(2)).Subscribed);
        Assert.True(session.Expired(now.AddSeconds(6)));
        Assert.False(session.Accept(new("uuid:one", 0), true, now.AddSeconds(6)));
        session.InvalidateLease(now);
        Assert.True(session.Expired(now));
    }
}
