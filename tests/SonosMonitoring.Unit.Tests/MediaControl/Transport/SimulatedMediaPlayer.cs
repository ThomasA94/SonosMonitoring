using System.Net;
using System.Xml.Linq;

namespace SonosMonitoring.Unit.Tests.MediaControl.Transport;

/// <summary>Returns fixed local UPnP responses and counts write actions.</summary>
internal sealed class SimulatedMediaPlayer : HttpMessageHandler
{
    /// <summary>Gets SOAP write requests observed by the fake.</summary>
    public int Writes { get; private set; }

    /// <summary>Gets optional per-address identities for multiroom protocol scenarios.</summary>
    public Dictionary<string, string> DeviceKeysByAddress { get; } = [];

    /// <summary>Gets or sets a complete multiroom topology shared by the simulated reporters.</summary>
    public string? TopologyXml { get; set; }

    /// <summary>Gets optional per-address household overrides.</summary>
    public Dictionary<string, string> HouseholdsByAddress { get; } = [];

    /// <summary>Gets optional per-player unsupported actions for heterogeneous capabilities.</summary>
    public Dictionary<string, string> OmittedActionsByAddress { get; } = [];

    /// <summary>Gets the target addresses and actions of actual SOAP writes.</summary>
    public List<(string Address, string Action)> WriteTargets { get; } = [];

    /// <summary>Gets or sets the identity returned by the live description endpoint.</summary>
    public string DescribedDeviceKey { get; set; } = "RINCON_ABC";

    /// <summary>Gets or sets the player placed in the current reported group.</summary>
    public string ReportedDeviceKey { get; set; } = "RINCON_ABC";

    /// <summary>Gets or sets the current transport URI reported to identity checks.</summary>
    public string CurrentUri { get; set; } = "x-rincon-queue:RINCON_ABC#0";

    /// <summary>Gets or sets the current ContentDirectory queue revision.</summary>
    public string QueueRevision { get; set; } = "5";

    /// <summary>Gets or sets the advertised current transport actions.</summary>
    public string CurrentTransportActions { get; set; } = "Play,Pause,Stop,Next,Previous,X_DLNA_SeekTime,X_DLNA_SeekTrackNr";

    /// <summary>Gets or sets one action omitted from its service contract.</summary>
    public string? OmittedServiceAction { get; set; }

    /// <summary>Responds to fixed protocol reads and records writes.</summary>
    /// <param name="request">Outgoing request.</param>
    /// <param name="cancellationToken">Unused fake cancellation.</param>
    /// <returns>One simulated HTTP response.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get)
        {
            string host = request.RequestUri!.Host;
            Assert.True(host == "127.0.0.1" || this.DeviceKeysByAddress.ContainsKey(host));
            if (request.RequestUri?.AbsolutePath == "/xml/device_description.xml")
            {
                return this.Response(this.Description().Replace(this.DescribedDeviceKey,
                    this.DeviceKeysByAddress.GetValueOrDefault(host, this.DescribedDeviceKey), StringComparison.Ordinal));
            }
            string path = request.RequestUri?.AbsolutePath ?? throw new InvalidOperationException("Missing service path.");
            string serviceName = path.Split('/')[^1].Replace("1.xml", "", StringComparison.Ordinal);
            return this.Response(this.ServiceDescription(serviceName, host));
        }
        string header = Assert.Single(request.Headers.GetValues("SOAPACTION"));
        string action = header.Trim('"').Split('#')[1];
        string service = header.Trim('"').Split(':')[^2].Split('#')[0];
        string requestBody = request.Content is null ? string.Empty :
            await request.Content.ReadAsStringAsync(cancellationToken);
        XDocument requestDocument = XDocument.Parse(requestBody);
        if (action is "SetVolume" or "SetMute" or "SetGroupVolume" or "SnapshotGroupVolume" or "SetGroupMute" or
            "SetAVTransportURI" or "Seek" or "Play" or "Pause" or "Stop" or "Next" or "Previous" or
            "SetPlayMode" or "SetCrossfadeMode" or "AddURIToQueue" or "RemoveTrackRangeFromQueue" or
            "ReorderTracksInQueue" or "RemoveAllTracksFromQueue" or "BecomeCoordinatorOfStandaloneGroup")
        {
            this.Writes++;
            this.WriteTargets.Add((request.RequestUri!.Host, action));
        }
        if (action == "SetAVTransportURI")
        {
            this.CurrentUri = requestDocument.Descendants().Single(element => element.Name.LocalName == "CurrentURI").Value;
        }
        string output = action switch
        {
            "GetHouseholdID" => "<CurrentHouseholdID>" + this.HouseholdsByAddress.GetValueOrDefault(request.RequestUri!.Host, "HH_1") + "</CurrentHouseholdID>",
            "GetZoneGroupState" => this.Topology(),
            "GetTransportInfo" => "<CurrentTransportState>PLAYING</CurrentTransportState>",
            "GetMediaInfo" => "<CurrentURI>" + System.Security.SecurityElement.Escape(this.CurrentUri) + "</CurrentURI>",
            "GetPositionInfo" => "<TrackDuration>0:03:00</TrackDuration><RelTime>00:00:05</RelTime>",
            "GetTransportSettings" => "<PlayMode>NORMAL</PlayMode>",
            "GetCrossfadeMode" => "<CrossfadeMode>0</CrossfadeMode>",
            "GetCurrentTransportActions" => "<Actions>" + this.CurrentTransportActions + "</Actions>",
            "GetVolume" => "<CurrentVolume>22</CurrentVolume>",
            "GetMute" => "<CurrentMute>0</CurrentMute>",
            "GetOutputFixed" => "<CurrentFixed>0</CurrentFixed>",
            "GetGroupMute" => "<CurrentMute>0</CurrentMute>",
            "GetGroupVolume" => "<CurrentVolume>40</CurrentVolume>",
            "Browse" => this.BrowseResult(requestDocument),
            "SetVolume" => "",
            "SetMute" => "",
            "SnapshotGroupVolume" => "",
            "SetGroupVolume" => "",
            "SetAVTransportURI" => "",
            "Seek" => "",
            "Play" => "",
            "SetPlayMode" => "",
            "RemoveTrackRangeFromQueue" => "",
            "RemoveAllTracksFromQueue" => "",
            "BecomeCoordinatorOfStandaloneGroup" => "",
            _ => throw new InvalidOperationException("Unexpected simulated action " + action)
        };
        string body = $"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:{action}Response xmlns:u=\"urn:schemas-upnp-org:service:{service}:1\">{output}</u:{action}Response></s:Body></s:Envelope>";
        return this.Response(body);
    }

    /// <summary>Wraps a fixed XML body in a successful HTTP response.</summary>
    /// <param name="xml">Response XML.</param>
    /// <returns>Fake HTTP response.</returns>
    private HttpResponseMessage Response(string xml) => new(HttpStatusCode.OK) { Content = new StringContent(xml) };

    /// <summary>Advertises only supported local services and a stable player UDN.</summary>
    /// <returns>Player description XML.</returns>
    private string Description() => $"<root><device><manufacturer>Sonos, Inc.</manufacturer><deviceType>urn:schemas-upnp-org:device:ZonePlayer:1</deviceType><UDN>uuid:{this.DescribedDeviceKey}</UDN>" +
        this.Service("ZoneGroupTopology", "/ZoneGroupTopology/Control") +
        this.Service("DeviceProperties", "/DeviceProperties/Control") +
        this.Service("AVTransport", "/MediaRenderer/AVTransport/Control") +
        this.Service("ContentDirectory", "/MediaServer/ContentDirectory/Control") +
        this.Service("GroupRenderingControl", "/MediaRenderer/GroupRenderingControl/Control") +
        this.Service("RenderingControl", "/MediaRenderer/RenderingControl/Control") + "</device></root>";

    /// <summary>Builds one exact advertised service/control pair.</summary>
    /// <param name="name">Known service.</param>
    /// <param name="path">Fixed control path.</param>
    /// <returns>Description fragment.</returns>
    private string Service(string name, string path) => $"<service><serviceType>urn:schemas-upnp-org:service:{name}:1</serviceType><controlURL>{path}</controlURL><SCPDURL>/xml/{name}1.xml</SCPDURL></service>";

    /// <summary>Reports one syntactically valid current group.</summary>
    /// <returns>Escaped topology output field.</returns>
    private string Topology()
    {
        string groups = $"<ZoneGroups><ZoneGroup ID=\"{this.ReportedDeviceKey}:0\" Coordinator=\"{this.ReportedDeviceKey}\"><ZoneGroupMember UUID=\"{this.ReportedDeviceKey}\" ZoneName=\"Study\" /></ZoneGroup></ZoneGroups>";
        return "<ZoneGroupState>" + System.Security.SecurityElement.Escape(this.TopologyXml ?? groups) + "</ZoneGroupState>";
    }

    /// <summary>Returns queue or favorite DIDL with the matching private playback metadata.</summary>
    /// <param name="request">Current Browse SOAP envelope.</param>
    /// <returns>Escaped SOAP output fields.</returns>
    private string BrowseResult(XDocument request)
    {
        bool favorite = request.Descendants().Single(element => element.Name.LocalName == "ObjectID").Value == "FV:2";
        string didl = favorite ?
            "<DIDL-Lite xmlns:r=\"urn:schemas-rinconnetworks-com:metadata-1-0/\"><item id=\"FV:2/1\"><title>Station</title><res>x-sonosapi-stream:station</res><r:resMD>&lt;DIDL-Lite&gt;&lt;item id=\"station\"&gt;&lt;title&gt;Station metadata&lt;/title&gt;&lt;/item&gt;&lt;/DIDL-Lite&gt;</r:resMD></item></DIDL-Lite>" :
            "<DIDL-Lite><item id=\"Q:0/1\"><title>Track</title><res>http://127.0.0.1:1400/track</res></item></DIDL-Lite>";
        return "<Result>" + System.Security.SecurityElement.Escape(didl) + "</Result><NumberReturned>1</NumberReturned><TotalMatches>1</TotalMatches><UpdateID>" + this.QueueRevision + "</UpdateID>";
    }

    /// <summary>Advertises the fixed actions exercised by the simulated player.</summary>
    /// <param name="service">Known service.</param>
    /// <param name="address">Player whose advertised contract is requested.</param>
    /// <returns>Bounded SCPD action list.</returns>
    private string ServiceDescription(string service, string address)
    {
        string[] actions = service switch
        {
            "AVTransport" => ["GetTransportInfo", "GetMediaInfo", "GetPositionInfo", "GetTransportSettings",
                "GetCrossfadeMode", "GetCurrentTransportActions", "Play", "Pause", "Stop", "Next", "Previous", "Seek",
                "SetPlayMode", "SetCrossfadeMode", "SetAVTransportURI", "AddURIToQueue", "RemoveTrackRangeFromQueue",
                "ReorderTracksInQueue", "RemoveAllTracksFromQueue", "BecomeCoordinatorOfStandaloneGroup"],
            "RenderingControl" => ["GetVolume", "GetMute", "GetOutputFixed", "SetVolume", "SetMute"],
            "ContentDirectory" => ["Browse"],
            "GroupRenderingControl" => ["GetGroupMute", "GetGroupVolume", "SnapshotGroupVolume", "SetGroupVolume", "SetGroupMute"],
            "DeviceProperties" => ["GetHouseholdID"],
            "ZoneGroupTopology" => ["GetZoneGroupState"],
            _ => throw new InvalidOperationException("Unexpected service description " + service)
        };
        return "<scpd><actionList>" + string.Concat(actions.Where(action => action != this.OmittedServiceAction &&
            action != this.OmittedActionsByAddress.GetValueOrDefault(address))
            .Select(action => $"<action><name>{action}</name></action>")) +
            "</actionList></scpd>";
    }
}
