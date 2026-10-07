using System.Xml.Linq;
using SonosMonitoring.Application.Collection;
using SonosMonitoring.Application.MediaControl.Transport;
using SonosMonitoring.Infrastructure.Collection;

namespace SonosMonitoring.Infrastructure.MediaControl.Transport;

/// <summary>Rejects stale, unknown, moved, or conflicting Sonos targets before local media calls.</summary>
public sealed class MediaTargetVerifier
{
    private readonly ICollectorSnapshotReader _reader;
    private readonly Func<CollectorOptions> _options;
    private readonly MediaSoapClient _soap;

    /// <summary>Creates a validator over the current collector generation.</summary>
    /// <param name="reader">Current detached observation snapshot.</param>
    /// <param name="options">Current collector configuration.</param>
    /// <param name="soap">Hardened local client.</param>
    public MediaTargetVerifier(ICollectorSnapshotReader reader, Func<CollectorOptions> options, MediaSoapClient soap)
    {
        this._reader = reader;
        this._options = options;
        this._soap = soap;
    }

    /// <summary>Revalidates every group reporter and its exact identity/household.</summary>
    /// <param name="deviceKey">Selected stable UDN.</param>
    /// <param name="cancellationToken">Validation cancellation.</param>
    /// <returns>Current target evidence.</returns>
    public async Task<ValidatedMediaTarget> ValidateAsync(string deviceKey, CancellationToken cancellationToken)
    {
        CollectorSnapshot snapshot = this._reader.GetSnapshot();
        CollectorOptions options = this._options();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (snapshot.Enabled == false || options.Enabled == false || snapshot.HeartbeatUtc is null ||
            now - snapshot.HeartbeatUtc > TimeSpan.FromSeconds(Math.Max(1, snapshot.HeartbeatMaxAgeSeconds)))
        {
            throw new InvalidOperationException("Collector evidence is unavailable or stale.");
        }

        Dictionary<string, DeviceSnapshot> devices = snapshot.Devices.ToDictionary(device => device.DeviceId, StringComparer.Ordinal);
        if (devices.TryGetValue(deviceKey, out DeviceSnapshot? selected) == false)
        {
            throw new InvalidOperationException("Unknown media target.");
        }

        this.CheckSnapshot(selected, now, options);
        (string household, IReadOnlySet<string> services) = await this.CheckIdentityAsync(selected, cancellationToken);
        if (services.Contains("ZoneGroupTopology") == false)
        {
            throw new InvalidOperationException("Topology control path is not advertised.");
        }
        IReadOnlyList<TopologyGroup> selectedGroups = await this.ReadTopologyAsync(selected, cancellationToken);
        TopologyGroup group = selectedGroups.SingleOrDefault(candidate => candidate.Members.Any(member => member.DeviceId == deviceKey))
            ?? throw new InvalidOperationException("Target is absent from current topology.");
        TopologyMember selectedMember = group.Members.Single(member => member.DeviceId == deviceKey);
        if (selectedMember.ParentDeviceId is not null || selectedMember.IsInvisible == true)
        {
            throw new InvalidOperationException("Satellite or bonded player cannot be controlled directly.");
        }

        string[] memberKeys = group.Members.Select(member => member.DeviceId).Order(StringComparer.Ordinal).ToArray();
        foreach (DeviceSnapshot reporter in snapshot.Devices)
        {
            if (reporter.TopologyStatus.LastAttemptSucceeded == false || reporter.TopologyStatus.IsStale ||
                reporter.TopologyStatus.LastSuccessUtc is null || now - reporter.TopologyStatus.LastSuccessUtc > options.StaleAfter)
            {
                continue;
            }
            TopologyGroup[] claims = reporter.Groups.Where(candidate =>
                candidate.Members.Any(member => member.DeviceId == deviceKey)).ToArray();
            if (claims.Length > 1 || claims.Any(candidate => candidate.GroupId != group.GroupId ||
                candidate.CoordinatorDeviceId != group.CoordinatorDeviceId ||
                candidate.Members.Select(member => member.DeviceId).Order(StringComparer.Ordinal).SequenceEqual(memberKeys) == false))
            {
                throw new InvalidOperationException("Fresh collector reporters disagree about the target group.");
            }
        }
        string? coordinatorAddress = null;
        IReadOnlySet<string>? coordinatorServices = null;
        foreach (string memberKey in memberKeys)
        {
            if (devices.TryGetValue(memberKey, out DeviceSnapshot? reporter) == false)
            {
                throw new InvalidOperationException("Group member is unknown to the collector.");
            }
            this.CheckSnapshot(reporter, now, options);
            IReadOnlySet<string> memberServices;
            if (reporter.DeviceId == selected.DeviceId)
            {
                memberServices = services;
            }
            else
            {
                (string memberHousehold, IReadOnlySet<string> offered) = await this.CheckIdentityAsync(reporter, cancellationToken);
                if (memberHousehold != household)
                {
                    throw new InvalidOperationException("Group crosses household identities.");
                }
                memberServices = offered;
            }

            if (memberServices.Contains("ZoneGroupTopology") == false)
            {
                throw new InvalidOperationException("Group reporter lacks the fixed topology control path.");
            }
            IReadOnlyList<TopologyGroup> view = reporter.DeviceId == selected.DeviceId ? selectedGroups :
                await this.ReadTopologyAsync(reporter, cancellationToken);
            TopologyGroup? reported = view.SingleOrDefault(candidate => candidate.Members.Any(member => member.DeviceId == deviceKey));
            if (reported is null || reported.GroupId != group.GroupId || reported.CoordinatorDeviceId != group.CoordinatorDeviceId ||
                reported.Members.Select(member => member.DeviceId).Order(StringComparer.Ordinal).SequenceEqual(memberKeys) == false)
            {
                throw new InvalidOperationException("Topology reporters disagree about the target group.");
            }
            if (memberKey == group.CoordinatorDeviceId)
            {
                coordinatorAddress = reporter.Address;
                coordinatorServices = memberServices;
            }
        }

        if (coordinatorAddress is null || coordinatorServices is null)
        {
            throw new InvalidOperationException("Group coordinator is unavailable.");
        }

        MediaTargetContext context = new(deviceKey, household, group.CoordinatorDeviceId, memberKeys, string.Empty);
        bool bonded = group.Members.Any(member => member.ParentDeviceId is not null || member.IsInvisible == true);
        return new ValidatedMediaTarget(context, selected.Address, coordinatorAddress, services, coordinatorServices, bonded);
    }

    /// <summary>Requires current collector identity and a still-approved literal address.</summary>
    /// <param name="device">Known physical player.</param>
    /// <param name="now">Validation time.</param>
    /// <param name="options">Current collector settings.</param>
    private void CheckSnapshot(DeviceSnapshot device, DateTimeOffset now, CollectorOptions options)
    {
        if (device.Description.LastAttemptSucceeded == false || device.Description.IsStale ||
            device.Description.LastSuccessUtc is null || now - device.Description.LastSuccessUtc > options.StaleAfter ||
            new Ipv4TargetPolicy(options.AllowedCidrs, options.AllowLoopback).IsAllowed(device.Address) == false)
        {
            throw new InvalidOperationException("Device identity evidence is stale or outside current policy.");
        }
    }

    /// <summary>Checks fresh UPnP UDN, offered paths, and household at the collector's address.</summary>
    /// <param name="device">Collector-known device.</param>
    /// <param name="cancellationToken">Read cancellation.</param>
    /// <returns>Household and advertised exact service pairs.</returns>
    private async Task<(string Household, IReadOnlySet<string> Services)> CheckIdentityAsync(DeviceSnapshot device,
        CancellationToken cancellationToken)
    {
        string xml = await this._soap.DescriptionAsync(device.Address, cancellationToken);
        PlayerDescription description = PlayerDescriptionParser.Parse(xml);
        if (description.DeviceId != device.DeviceId)
        {
            throw new InvalidOperationException("Player identity changed at the current address.");
        }

        XDocument document = MediaSoapProtocol.ReadDocument(xml);
        HashSet<string> services = new(StringComparer.Ordinal);
        foreach (XElement service in document.Descendants().Where(element => element.Name.LocalName == "service"))
        {
            string? type = service.Elements().FirstOrDefault(element => element.Name.LocalName == "serviceType")?.Value;
            string? control = service.Elements().FirstOrDefault(element => element.Name.LocalName == "controlURL")?.Value;
            string? descriptionPath = service.Elements().FirstOrDefault(element => element.Name.LocalName == "SCPDURL")?.Value;
            foreach (string name in new[] { "AVTransport", "RenderingControl", "GroupRenderingControl", "ContentDirectory", "DeviceProperties", "ZoneGroupTopology" })
            {
                string path = name switch
                {
                    "AVTransport" or "RenderingControl" or "GroupRenderingControl" => $"/MediaRenderer/{name}/Control",
                    "ContentDirectory" => "/MediaServer/ContentDirectory/Control",
                    "ZoneGroupTopology" => "/ZoneGroupTopology/Control",
                    _ => "/DeviceProperties/Control"
                };
                string requiredDescriptionPath = $"/xml/{name}1.xml";
                if (type == $"urn:schemas-upnp-org:service:{name}:1" &&
                    (control == path || control == path[1..]) &&
                    (descriptionPath == requiredDescriptionPath || descriptionPath == requiredDescriptionPath[1..]))
                {
                    services.Add(name);
                }
            }
        }
        if (services.Contains("DeviceProperties") == false)
        {
            throw new InvalidOperationException("Household service is not advertised.");
        }
        string householdContract = await this._soap.ServiceDescriptionAsync(device.Address, "DeviceProperties", cancellationToken);
        if (MediaServiceContractParser.ParseActions(householdContract).Contains("GetHouseholdID") == false)
        {
            throw new InvalidOperationException("Household action is not advertised.");
        }
        IReadOnlyDictionary<string, string> householdFields = await this._soap.CallAsync(device.Address,
            "DeviceProperties", "GetHouseholdID", new Dictionary<string, string>(), cancellationToken);
        string household = householdFields.GetValueOrDefault("CurrentHouseholdID") ?? string.Empty;
        if (household.Length is < 1 or > 128 || household.Any(character => char.IsAsciiLetterOrDigit(character) == false && character is not ('_' or '-')))
        {
            throw new FormatException("Invalid Sonos household identity.");
        }
        return (household, services);
    }

    /// <summary>Reads current topology from one freshly verified reporter.</summary>
    /// <param name="device">Reporter.</param>
    /// <param name="cancellationToken">Read cancellation.</param>
    /// <returns>Parsed reporter view.</returns>
    private async Task<IReadOnlyList<TopologyGroup>> ReadTopologyAsync(DeviceSnapshot device, CancellationToken cancellationToken)
    {
        string topologyContract = await this._soap.ServiceDescriptionAsync(device.Address, "ZoneGroupTopology",
            cancellationToken);
        if (MediaServiceContractParser.ParseActions(topologyContract).Contains("GetZoneGroupState") == false)
        {
            throw new InvalidOperationException("Topology action is not advertised.");
        }
        IReadOnlyDictionary<string, string> fields = await this._soap.CallAsync(device.Address,
            "ZoneGroupTopology", "GetZoneGroupState", new Dictionary<string, string>(), cancellationToken);
        if (fields.TryGetValue("ZoneGroupState", out string? state) == false)
        {
            throw new FormatException("Topology output is missing.");
        }
        // The collector parser owns topology structure; wrap the already-validated scalar result.
        string wrapped = $"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:GetZoneGroupStateResponse xmlns:u=\"urn:schemas-upnp-org:service:ZoneGroupTopology:1\"><ZoneGroupState>{System.Security.SecurityElement.Escape(state)}</ZoneGroupState></u:GetZoneGroupStateResponse></s:Body></s:Envelope>";
        return ZoneGroupTopologyParser.Parse(wrapped, this._options().MaxKnownDevices);
    }
}
