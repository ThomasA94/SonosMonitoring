using System.Security.Cryptography;
using SonosMonitoring.Application.Collection;

namespace SonosMonitoring.Infrastructure.Collection.Gena;

/// <summary>Serializes lease and sequence evidence while coalescing notifications into bounded poll requests.</summary>
internal sealed class GenaSession
{
    private readonly object _gate = new();
    private string? _sid;
    private string? _provisionalSid;
    private uint? _sequence;
    private DateTimeOffset? _expires;
    private DateTimeOffset? _lastAttempt;
    private DateTimeOffset? _lastNotification;
    private DateTimeOffset _nextAttempt;
    private DateTimeOffset _lastPoll;
    private DateTimeOffset _subscribedAt;
    private bool _pollPending;
    private string? _error;
    private int _failures;

    /// <summary>Creates an isolated callback route for one validated player and address.</summary>
    /// <param name="id">Validated player identity.</param>
    /// <param name="address">Current validated address.</param>
    public GenaSession(string id, string address)
    {
        this.DeviceId = id;
        this.Address = address;
        this.Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }

    /// <summary>Gets the admitted player identity.</summary>
    public string DeviceId { get; }

    /// <summary>Gets the only accepted callback peer address.</summary>
    public string Address { get; }

    /// <summary>Gets the ephemeral capability used solely in the callback URL.</summary>
    public string Token { get; }

    /// <summary>Gets the confirmed subscription identifier for renewal or cleanup.</summary>
    public string? Sid { get { lock (this._gate) { return this._sid; } } }

    /// <summary>Checks whether a confirmed finite lease must be replaced.</summary>
    /// <param name="now">Current UTC time.</param>
    public bool Expired(DateTimeOffset now) { lock (this._gate) { return this._expires <= now; } }

    /// <summary>Retries a lease whose initial callback never proved the configured return path.</summary>
    /// <param name="now">Current UTC time.</param>
    /// <param name="timeout">Configured initial callback budget.</param>
    public bool InitialMissing(DateTimeOffset now, TimeSpan timeout)
    {
        lock (this._gate)
        {
            return this._sid is not null && this._sequence is null &&
                now - this._subscribedAt >= TimeSpan.FromSeconds(Math.Max(5, timeout.TotalSeconds * 2));
        }
    }

    /// <summary>Retires a confirmed lease rejected by the publisher.</summary>
    /// <param name="now">Rejection time.</param>
    public void InvalidateLease(DateTimeOffset now) { lock (this._gate) { this._expires = now; } }

    /// <summary>Checks the next subscription or retry deadline.</summary>
    /// <param name="now">Current UTC time.</param>
    public bool Due(DateTimeOffset now) { lock (this._gate) { return now >= this._nextAttempt; } }

    /// <summary>Records an actual outbound subscription attempt.</summary>
    /// <param name="now">Attempt UTC time.</param>
    public void BeginAttempt(DateTimeOffset now)
    {
        lock (this._gate)
        {
            this._lastAttempt = now;
            if (this._sid is null)
            {
                this._provisionalSid = null;
                this._sequence = null;
                this._lastNotification = null;
                this._pollPending = false;
            }
        }
    }

    /// <summary>Confirms response identity, including a possible early initial callback.</summary>
    /// <param name="grant">Device-granted lease.</param>
    /// <param name="now">Response UTC time.</param>
    /// <param name="requestedSeconds">Local maximum interval before trying a renewal of an infinite lease.</param>
    public void Confirm(GenaLeaseGrant grant, DateTimeOffset now, int requestedSeconds)
    {
        lock (this._gate)
        {
            if (this._sid is null)
            {
                this._subscribedAt = now;
                if (this._provisionalSid != grant.Sid)
                {
                    this._sequence = null;
                    this._lastNotification = null;
                    this._pollPending = false;
                }
            }
            this._sid = grant.Sid;
            this._expires = grant.Duration is { } duration ? now + duration : null;
            double seconds = Math.Min(grant.Duration?.TotalSeconds ?? requestedSeconds, requestedSeconds);
            this._nextAttempt = now + TimeSpan.FromSeconds(seconds * 0.5);
            this._failures = 0;
            this._error = null;
        }
    }

    /// <summary>Keeps a still-valid lease during temporary renewal failure and bounds retries.</summary>
    /// <param name="now">Failure time.</param>
    public void Failed(DateTimeOffset now)
    {
        lock (this._gate)
        {
            this._error = "subscription_failed";
            this._failures = Math.Min(6, this._failures + 1);
            this._nextAttempt = now + TimeSpan.FromSeconds(Math.Pow(2, this._failures) + Random.Shared.NextDouble());
        }
    }

    /// <summary>Validates per-lease ordering without applying a possibly delayed event payload to live state.</summary>
    /// <param name="headers">Validated event headers.</param>
    /// <param name="topologyChanged">Whether the propertyset contains a topology hint.</param>
    /// <param name="now">Receive time.</param>
    /// <returns>Whether the event belongs to this lease; duplicates are acknowledged without side effects.</returns>
    public bool Accept(GenaHeaders headers, bool topologyChanged, DateTimeOffset now)
    {
        lock (this._gate)
        {
            if (this._expires <= now || (this._sid is not null && headers.Sid != this._sid))
            {
                return false;
            }
            if (this._sid is null && (headers.Sequence != 0 || (this._provisionalSid is not null && headers.Sid != this._provisionalSid)))
            {
                return false;
            }
            if (this._sequence is uint previous)
            {
                if (headers.Sequence == previous || headers.Sequence == 0 || unchecked((int)(headers.Sequence - previous)) < 0)
                {
                    return true;
                }
                this._error = (previous == 0 && headers.Sequence == 1) || GenaHeaders.IsSuccessor(previous, headers.Sequence) ? null : "sequence_gap";
            }
            else if (headers.Sequence != 0)
            {
                return false;
            }
            this._provisionalSid = headers.Sid;
            this._sequence = headers.Sequence;
            this._lastNotification = now;
            this._pollPending |= topologyChanged || this._error == "sequence_gap";
            return true;
        }
    }

    /// <summary>Consumes at most one coalesced reconciliation per configured interval.</summary>
    /// <param name="now">Current time.</param>
    /// <param name="interval">Minimum time between event-triggered polls.</param>
    public bool TakePoll(DateTimeOffset now, TimeSpan interval)
    {
        lock (this._gate)
        {
            if (this._sid is null || this._pollPending == false || now - this._lastPoll < interval)
            {
                return false;
            }
            this._pollPending = false;
            this._lastPoll = now;
            return true;
        }
    }

    /// <summary>Builds public evidence; silence after a valid initial callback does not indicate failure.</summary>
    /// <param name="now">Current time.</param>
    /// <param name="initialTimeout">Allowed wait for initial callback proof.</param>
    public GenaSnapshot Snapshot(DateTimeOffset now, TimeSpan initialTimeout)
    {
        lock (this._gate)
        {
            bool subscribed = this._sid is not null && (this._expires is null || this._expires > now);
            bool received = subscribed && this._sequence.HasValue;
            string? error = this._error;
            if (subscribed && received == false && error is null)
            {
                error = now - this._subscribedAt >= initialTimeout ? "callback_unconfirmed" : "awaiting_initial";
            }
            return new GenaSnapshot(true, subscribed, this._lastAttempt, this._expires, this._lastNotification, received, error);
        }
    }
}
