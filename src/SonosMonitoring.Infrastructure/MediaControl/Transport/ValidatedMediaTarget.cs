using SonosMonitoring.Application.MediaControl.Transport;

namespace SonosMonitoring.Infrastructure.MediaControl.Transport;

/// <summary>Fresh identity, household, topology, and service evidence for a selected player.</summary>
/// <param name="Context">Validated context without source fingerprint.</param>
/// <param name="SelectedAddress">Selected player's approved literal address.</param>
/// <param name="CoordinatorAddress">Coordinator's approved literal address.</param>
/// <param name="Services">Advertised exact local service/control pairs for the selected player.</param>
/// <param name="CoordinatorServices">Advertised exact pairs for the coordinator.</param>
/// <param name="HasBondedMembers">Whether the group includes an invisible or nested satellite member.</param>
public sealed record ValidatedMediaTarget(MediaTargetContext Context, string SelectedAddress, string CoordinatorAddress,
    IReadOnlySet<string> Services, IReadOnlySet<string> CoordinatorServices, bool HasBondedMembers);
