namespace SonosMonitoring.Application.MediaControl;

/// <summary>Discovers bounded reconciliation hints; authoritative decisions must reread event and coordination state.</summary>
public interface IMediaAdmissionCandidates
{
    /// <summary>Reads accepted operation identities in canonical identity order, excluding installation decisions.</summary>
    /// <param name="after">Exclusive canonical identity cursor, or null to begin another sweep.</param>
    /// <param name="ct">Bounded query cancellation.</param>
    /// <returns>At most 32 canonical operation identities; an empty page ends the sweep.</returns>
    Task<IReadOnlyList<string>> ReadAsync(string? after, CancellationToken ct);
}
