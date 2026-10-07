namespace SonosMonitoring.Application.MediaControl;

/// <summary>Persists technical admission and execution fences separately from domain facts.</summary>
/// <param name="OperationId">Stable user action identity.</param>
/// <param name="Position">Durable admission order.</param>
/// <param name="Fingerprint">Immutable request fingerprint.</param>
/// <param name="Generation">Original authorization generation.</param>
/// <param name="Targets">All conflicting stable player identities.</param>
/// <param name="PayloadJson">Immutable application descriptor.</param>
/// <param name="AcceptedAtUtc">Original acceptance time.</param>
/// <param name="ExpiresAtUtc">Dispatch deadline.</param>
/// <param name="Status">Technical lifecycle state.</param>
/// <param name="Step">Last irrevocably released write step.</param>
/// <param name="Code">Safe outcome code.</param>
public sealed record CoordinatedMediaOperation(Guid OperationId, long Position, string Fingerprint, string Generation,
    string[] Targets, string PayloadJson, DateTimeOffset AcceptedAtUtc, DateTimeOffset ExpiresAtUtc,
    string Status = "accepted", int Step = -1, string? Code = null);
