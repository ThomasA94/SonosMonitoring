using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Xml.Linq;

namespace SonosMonitoring.E2E.Tests.Collection;

/// <summary>Serves two isolated loopback players; only the second receives injected failures.</summary>
public sealed class SimulatedPlayer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private long _counter = 1000;
    private string _fault = "";
    private string _topologyFault = "";
    private int _splitGroups;
    private int _unexpectedRequests;
    private int _topologyRequests;
    private int _secondAvailable = 1;
    private string _secondAddress = "127.0.0.3";
    private string? _reportedSecondLocation;
    private string? _extraTopologyLocation;
    private string _reportedSecondId = "RINCON_SECOND";
    private int _blockedTargetRequests;
    private string _diagnosticFault = "";
    private string _sourceFault = "";
    private int _secondSupportsPlayback = 1;
    private int _secondSupportsRendering = 1;
    private readonly ConcurrentDictionary<string, int> _diagnosticRequests = new(StringComparer.Ordinal);

    /// <summary>Builds the private simulator; no real Sonos target is contacted.</summary>
    public SimulatedPlayer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration["AllowedHosts"] = "127.0.0.2;127.0.0.3;127.0.0.4;127.0.0.5";
        builder.WebHost.UseUrls("http://127.0.0.2:1400", "http://127.0.0.3:1400", "http://127.0.0.4:1400", "http://127.0.0.5:1400");
        this._app = builder.Build();
        this._app.MapGet("/xml/device_description.xml", this.DescriptionAsync);
        this._app.MapGet("/status/ifconfig", this.CountersAsync);
        this._app.MapGet("/status/wireless", this.WirelessAsync);
        this._app.MapGet("/status/enetports", this.EthernetAsync);
        this._app.MapPost("/MediaRenderer/AVTransport/Control", this.PlaybackAsync);
        this._app.MapPost("/MediaRenderer/RenderingControl/Control", this.RenderingAsync);
        this._app.MapPost("/ZoneGroupTopology/Control", this.TopologyAsync);
        this._app.MapGet("/unexpected", () => Interlocked.Increment(ref this._unexpectedRequests));
    }

    /// <summary>Gets or sets the fault applied to the second simulated device.</summary>
    public string Fault { get => Volatile.Read(ref this._fault); set => Volatile.Write(ref this._fault, value); }

    /// <summary>Gets or sets an independent topology failure on the second player.</summary>
    public string TopologyFault { get => Volatile.Read(ref this._topologyFault); set => Volatile.Write(ref this._topologyFault, value); }

    /// <summary>Gets or sets the diagnostic fault applied only to the second simulated player.</summary>
    public string DiagnosticFault { get => Volatile.Read(ref this._diagnosticFault); set => Volatile.Write(ref this._diagnosticFault, value); }

    /// <summary>Gets or sets which diagnostic source receives the second-player fault.</summary>
    public string SourceFault { get => Volatile.Read(ref this._sourceFault); set => Volatile.Write(ref this._sourceFault, value); }

    /// <summary>Gets or sets whether the second device description advertises AVTransport.</summary>
    public bool SecondSupportsPlayback { get => Volatile.Read(ref this._secondSupportsPlayback) == 1; set => Volatile.Write(ref this._secondSupportsPlayback, value ? 1 : 0); }

    /// <summary>Gets or sets whether the second device description advertises RenderingControl.</summary>
    public bool SecondSupportsRendering { get => Volatile.Read(ref this._secondSupportsRendering) == 1; set => Volatile.Write(ref this._secondSupportsRendering, value ? 1 : 0); }

    /// <summary>Gets or sets whether the simulated group has been split.</summary>
    public bool SplitGroups { get => Volatile.Read(ref this._splitGroups) == 1; set => Volatile.Write(ref this._splitGroups, value ? 1 : 0); }

    /// <summary>Gets requests following an untrusted member location or redirect.</summary>
    public int UnexpectedRequests => Volatile.Read(ref this._unexpectedRequests);

    /// <summary>Gets topology requests received by either simulated player.</summary>
    public int TopologyRequests => Volatile.Read(ref this._topologyRequests);

    /// <summary>Gets the number of requests received for a named diagnostic source.</summary>
    /// <param name="source">Wireless, ethernet, playback, volume, or mute.</param>
    public int DiagnosticRequests(string source) => this._diagnosticRequests.TryGetValue(source, out int count) ? count : 0;

    /// <summary>Gets diagnostic request count for one simulated player address.</summary>
    /// <param name="source">Wireless, ethernet, playback, volume, or mute.</param>
    /// <param name="address">The listener address, such as 127.0.0.3.</param>
    public int DiagnosticRequests(string source, string address) =>
        this._diagnosticRequests.TryGetValue(address + ":" + source, out int count) ? count : 0;

    /// <summary>Gets or sets whether the second player can currently be identified.</summary>
    public bool SecondAvailable { get => Volatile.Read(ref this._secondAvailable) == 1; set => Volatile.Write(ref this._secondAvailable, value ? 1 : 0); }

    /// <summary>Gets or sets the address currently hosting the same second-player UDN.</summary>
    public string SecondAddress { get => Volatile.Read(ref this._secondAddress); set => Volatile.Write(ref this._secondAddress, value); }

    /// <summary>Gets or sets the untrusted second-player description URL reported through topology.</summary>
    public string? ReportedSecondLocation { get => Volatile.Read(ref this._reportedSecondLocation); set => Volatile.Write(ref this._reportedSecondLocation, value); }

    /// <summary>Gets or sets an additional untrusted member location for destination-boundary tests.</summary>
    public string? ExtraTopologyLocation { get => Volatile.Read(ref this._extraTopologyLocation); set => Volatile.Write(ref this._extraTopologyLocation, value); }

    /// <summary>Gets or sets the advertised UDN independently of the actual HTTP device identity.</summary>
    public string ReportedSecondId { get => Volatile.Read(ref this._reportedSecondId); set => Volatile.Write(ref this._reportedSecondId, value); }

    /// <summary>Gets HTTP identification calls to the out-of-scope sentinel address.</summary>
    public int BlockedTargetRequests => Volatile.Read(ref this._blockedTargetRequests);

    /// <summary>Starts the simulator's bounded local listeners.</summary>
    public Task StartAsync() => this._app.StartAsync();

    /// <summary>Stops listeners and waits for all in-flight requests to finish.</summary>
    public async ValueTask DisposeAsync()
    {
        await this._app.StopAsync();
        await this._app.DisposeAsync();
    }

    /// <summary>Returns a valid device identity, or malformed XML for the parser-failure scenario.</summary>
    /// <param name="context">Simulator request.</param>
    private async Task DescriptionAsync(HttpContext context)
    {
        string address = context.Connection.LocalIpAddress!.ToString();
        if (address == "127.0.0.5")
        {
            Interlocked.Increment(ref this._blockedTargetRequests);
        }
        var second = address != "127.0.0.2";
        if (second && (this.SecondAvailable == false || address != this.SecondAddress))
        {
            context.Response.StatusCode = 404;
            return;
        }
        if (second && this.Fault == "xml")
        {
            await context.Response.WriteAsync("<root><broken>");
            return;
        }
        if (second && this.Fault == "description_disconnect")
        {
            context.Response.ContentLength = 2000;
            await context.Response.WriteAsync("<incomplete>");
            await context.Response.Body.FlushAsync();
            context.Abort();
            return;
        }
        string playbackService = second && this.SecondSupportsPlayback == false ? string.Empty :
            "<service><serviceType>urn:schemas-upnp-org:service:AVTransport:1</serviceType><controlURL>/MediaRenderer/AVTransport/Control</controlURL></service>";
        string renderingService = second && this.SecondSupportsRendering == false ? string.Empty :
            "<service><serviceType>urn:schemas-upnp-org:service:RenderingControl:1</serviceType><controlURL>/MediaRenderer/RenderingControl/Control</controlURL></service>";
        context.Response.ContentType = "text/xml";
        await context.Response.WriteAsync($"""
            <root xmlns="urn:schemas-upnp-org:device-1-0"><device>
            <deviceType>urn:schemas-upnp-org:device:ZonePlayer:1</deviceType>
            <manufacturer>Sonos, Inc.</manufacturer><modelName>Fixture</modelName><roomName>Test</roomName>
            <softwareVersion>86.10-80260</softwareVersion><hardwareVersion>1.20.2.3-2.0</hardwareVersion><modelNumber>A100</modelNumber>
            <UDN>uuid:RINCON_{(second ? "SECOND" : "FIRST")}</UDN>
            <serviceList>
              <service><serviceType>urn:schemas-upnp-org:service:ZoneGroupTopology:1</serviceType></service>
              {playbackService}
              {renderingService}
            </serviceList>
            </device></root>
            """);
    }

    /// <summary>Validates the read-only SOAP action and serves topology changes or independent faults.</summary>
    /// <param name="context">Simulator HTTP request.</param>
    private async Task TopologyAsync(HttpContext context)
    {
        Interlocked.Increment(ref this._topologyRequests);
        using StreamReader reader = new(context.Request.Body);
        string body = await reader.ReadToEndAsync(context.RequestAborted);
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace service = "urn:schemas-upnp-org:service:ZoneGroupTopology:1";
        if (context.Request.Headers["SOAPACTION"] != "\"urn:schemas-upnp-org:service:ZoneGroupTopology:1#GetZoneGroupState\"" ||
            context.Request.ContentType?.StartsWith("text/xml", StringComparison.Ordinal) != true ||
            XDocument.Parse(body).Root?.Element(soap + "Body")?.Element(service + "GetZoneGroupState") is null)
        {
            context.Response.StatusCode = 400;
            return;
        }

        if (context.Connection.LocalIpAddress!.ToString() == "127.0.0.3")
        {
            switch (this.TopologyFault)
            {
                case "disconnect":
                    context.Response.ContentLength = 2000;
                    await context.Response.WriteAsync("<incomplete>");
                    await context.Response.Body.FlushAsync();
                    context.Abort();
                    return;
                case "timeout":
                    await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted);
                    return;
                case "503":
                    context.Response.StatusCode = 503;
                    return;
                case "redirect":
                    context.Response.Redirect("http://127.0.0.2:1400/unexpected");
                    return;
                case "oversize":
                    await context.Response.WriteAsync(new string('x', 100000));
                    return;
                case "invalid":
                    await context.Response.WriteAsync("<broken>");
                    return;
                case "soap_fault":
                    await context.Response.WriteAsync($"<s:Envelope xmlns:s=\"{soap}\"><s:Body><s:Fault /></s:Body></s:Envelope>");
                    return;
            }
        }

        XElement secondMember = new("ZoneGroupMember", new XAttribute("UUID", this.ReportedSecondId),
            new XAttribute("ZoneName", "Second"), new XAttribute("Invisible", "0"));
        if (this.ReportedSecondLocation is string secondLocation)
        {
            secondMember.Add(new XAttribute("Location", secondLocation));
        }
        string second = secondMember.ToString(SaveOptions.DisableFormatting);
        if (this.ExtraTopologyLocation is string extraLocation)
        {
            second += new XElement("ZoneGroupMember", new XAttribute("UUID", "RINCON_OUTSIDE"),
                new XAttribute("Location", extraLocation)).ToString(SaveOptions.DisableFormatting);
        }
        string satellite = "<Satellite UUID=\"RINCON_SATELLITE\" Invisible=\"1\" Location=\"http://127.0.0.2:1400/unexpected\" />";
        string first = $"<ZoneGroupMember UUID=\"RINCON_FIRST\" ZoneName=\"First\" Invisible=\"0\">{satellite}</ZoneGroupMember>";
        string topology = this.SplitGroups
            ? $"<ZoneGroups><ZoneGroup ID=\"first\" Coordinator=\"RINCON_FIRST\">{first}</ZoneGroup><ZoneGroup ID=\"second\" Coordinator=\"RINCON_SECOND\">{second}</ZoneGroup></ZoneGroups>"
            : $"<ZoneGroups><ZoneGroup ID=\"together\" Coordinator=\"RINCON_FIRST\">{first}{second}</ZoneGroup></ZoneGroups>";
        XElement response = new(soap + "Envelope", new XElement(soap + "Body",
            new XElement(service + "GetZoneGroupStateResponse", new XElement("ZoneGroupState", topology))));
        context.Response.ContentType = "text/xml";
        await context.Response.WriteAsync(response.ToString(SaveOptions.DisableFormatting));
    }

    /// <summary>Returns evolving counters or one explicitly selected transient/permanent failure.</summary>
    /// <param name="context">Simulator request and cancellation.</param>
    private async Task CountersAsync(HttpContext context)
    {
        if (context.Connection.LocalIpAddress!.ToString() == "127.0.0.3")
        {
            switch (this.Fault)
            {
                case "timeout":
                    await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted);
                    return;
                case "disconnect":
                    context.Abort();
                    return;
                case "429":
                    context.Response.StatusCode = 429;
                    context.Response.Headers.RetryAfter = "120";
                    return;
                case "503":
                    context.Response.StatusCode = 503;
                    return;
                case "oversize":
                    await context.Response.WriteAsync(new string('x', 100000));
                    return;
                case "invalid":
                    await context.Response.WriteAsync("<Fault>not counters</Fault>");
                    return;
            }
        }
        var bytes = Interlocked.Increment(ref this._counter);
        await context.Response.WriteAsync($"""
            br0       Link encap:Ethernet
                      RX packets:10 errors:0 dropped:2 overruns:0 frame:0
                      TX packets:20 errors:0 dropped:0 overruns:0 carrier:0
                      RX bytes:{bytes} TX bytes:4000

            """);
    }

    /// <summary>Serves a fixed wireless report or a source-specific fault for the second player.</summary>
    /// <param name="context">Simulator request.</param>
    private async Task WirelessAsync(HttpContext context)
    {
        this.CountDiagnostic(context, "wireless");
        if (await this.ApplyDiagnosticFaultAsync(context, "wireless"))
        {
            return;
        }

        await context.Response.WriteAsync("<ZPSupportInfo><Wireless><WirelessInfo Name=\"Wireless Info\"><WifiModeString>STATION_MODE</WifiModeString><ConnectionTypeString>WiFi</ConnectionTypeString><SonosNetDisabled>0</SonosNetDisabled></WirelessInfo></Wireless></ZPSupportInfo>");
    }

    /// <summary>Serves one wired-port report for either player.</summary>
    /// <param name="context">Simulator request.</param>
    private async Task EthernetAsync(HttpContext context)
    {
        this.CountDiagnostic(context, "ethernet");
        if (await this.ApplyDiagnosticFaultAsync(context, "ethernet"))
        {
            return;
        }

        await context.Response.WriteAsync("<ZPSupportInfo><EnetPorts><Port port=\"0\"><Link>0</Link><Speed>0</Speed></Port></EnetPorts></ZPSupportInfo>");
    }

    /// <summary>Validates and answers only a read-only GetTransportInfo action.</summary>
    /// <param name="context">Simulator SOAP request.</param>
    private async Task PlaybackAsync(HttpContext context)
    {
        this.CountDiagnostic(context, "playback");
        if (await this.ValidateDiagnosticSoapAsync(context, "AVTransport", "GetTransportInfo"))
        {
            return;
        }
        if (await this.ApplyDiagnosticFaultAsync(context, "playback"))
        {
            return;
        }

        await this.WriteSoapResponseAsync(context, "AVTransport", "GetTransportInfoResponse",
            "<CurrentTransportState>STOPPED</CurrentTransportState><CurrentTransportStatus>OK</CurrentTransportStatus><CurrentSpeed>1</CurrentSpeed>");
    }

    /// <summary>Validates and answers only GetVolume or GetMute without changing playback state.</summary>
    /// <param name="context">Simulator SOAP request.</param>
    private async Task RenderingAsync(HttpContext context)
    {
        using StreamReader reader = new(context.Request.Body);
        string body = await reader.ReadToEndAsync(context.RequestAborted);
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace service = "urn:schemas-upnp-org:service:RenderingControl:1";
        XElement? envelope = XDocument.Parse(body).Root;
        XElement? bodyElement = envelope?.Element(soap + "Body");
        XElement? action = bodyElement?.Elements().SingleOrDefault();
        string? actionName = action?.Name == service + "GetVolume" ? "GetVolume" :
            action?.Name == service + "GetMute" ? "GetMute" : null;
        if (actionName is null || this.ValidateDiagnosticSoap(context, "RenderingControl", actionName, body) == false)
        {
            context.Response.StatusCode = 400;
            return;
        }

        string source = actionName == "GetVolume" ? "volume" : "mute";
        this.CountDiagnostic(context, source);
        if (await this.ApplyDiagnosticFaultAsync(context, "volume"))
        {
            return;
        }

        if (actionName == "GetVolume")
        {
            string output = this.IsSecond(context) && this.SourceFault == "volume" && this.DiagnosticFault == "partial_volume"
                ? "<CurrentVolume>34x</CurrentVolume>" : "<CurrentVolume>34</CurrentVolume>";
            await this.WriteSoapResponseAsync(context, "RenderingControl", "GetVolumeResponse", output);
            return;
        }

        string mute = this.IsSecond(context) && this.SourceFault == "volume" && this.DiagnosticFault == "partial_mute"
            ? "<CurrentMute>0x</CurrentMute>" : "<CurrentMute>0</CurrentMute>";
        await this.WriteSoapResponseAsync(context, "RenderingControl", "GetMuteResponse", mute);
    }

    /// <summary>Applies configured second-player faults to one diagnostic source.</summary>
    /// <param name="context">The request receiving the fault.</param>
    /// <param name="source">The logical diagnostic source.</param>
    /// <returns>True when a fault response was applied.</returns>
    private async Task<bool> ApplyDiagnosticFaultAsync(HttpContext context, string source)
    {
        if (this.IsSecond(context) == false || this.SourceFault != source)
        {
            return false;
        }

        switch (this.DiagnosticFault)
        {
            case "timeout":
                await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted);
                return true;
            case "503":
                context.Response.StatusCode = 503;
                return true;
            case "404":
                context.Response.StatusCode = 404;
                return true;
            case "malformed":
                await context.Response.WriteAsync("<ZPSupportInfo><broken>");
                return true;
            case "soap_fault" when source == "playback":
                await context.Response.WriteAsync("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault /></s:Body></s:Envelope>");
                return true;
            case "partial_volume" when source == "volume":
            case "partial_mute" when source == "volume":
                return false;
            default:
                return false;
        }
    }

    /// <summary>Reads the SOAP body and validates the expected action and header.</summary>
    /// <param name="context">The incoming request.</param>
    /// <param name="serviceName">AVTransport or RenderingControl.</param>
    /// <param name="actionName">The only supported read action.</param>
    /// <returns>True after writing an error for an invalid request.</returns>
    private async Task<bool> ValidateDiagnosticSoapAsync(HttpContext context, string serviceName, string actionName)
    {
        using StreamReader reader = new(context.Request.Body);
        string body = await reader.ReadToEndAsync(context.RequestAborted);
        if (this.ValidateDiagnosticSoap(context, serviceName, actionName, body))
        {
            return false;
        }

        context.Response.StatusCode = 400;
        return true;
    }

    /// <summary>Checks the exact known SOAP action envelope and required InstanceID.</summary>
    /// <param name="context">The incoming request.</param>
    /// <param name="serviceName">The service name.</param>
    /// <param name="actionName">The action name.</param>
    /// <param name="body">The parsed request body.</param>
    /// <returns>Whether the request matches the fixed supported contract.</returns>
    private bool ValidateDiagnosticSoap(HttpContext context, string serviceName, string actionName, string body)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace service = $"urn:schemas-upnp-org:service:{serviceName}:1";
        string actionHeader = $"\"urn:schemas-upnp-org:service:{serviceName}:1#{actionName}\"";
        XElement? envelope = XDocument.Parse(body).Root;
        XElement? bodyElement = envelope?.Element(soap + "Body");
        XElement? action = bodyElement?.Elements().SingleOrDefault();
        bool validInputs = action is not null && (serviceName == "RenderingControl"
            ? action.Elements("InstanceID").SingleOrDefault()?.Value == "0" &&
                action.Elements("Channel").SingleOrDefault()?.Value == "Master" && action.Elements().Count() == 2
            : action.Elements("InstanceID").SingleOrDefault()?.Value == "0" && action.Elements().Count() == 1);
        return context.Request.Method == "POST" &&
            context.Request.Headers["SOAPACTION"] == actionHeader &&
            context.Request.ContentType?.StartsWith("text/xml", StringComparison.OrdinalIgnoreCase) == true &&
            envelope is not null && envelope.Name == soap + "Envelope" && envelope.Elements(soap + "Body").Count() == 1 &&
            bodyElement is not null && bodyElement.Elements().Count() == 1 && action?.Name == service + actionName && validInputs;
    }

    /// <summary>Writes a bounded SOAP action response using the matching UPnP service namespace.</summary>
    /// <param name="context">The outgoing response.</param>
    /// <param name="serviceName">The service name.</param>
    /// <param name="actionName">The response action name.</param>
    /// <param name="output">The scalar action output.</param>
    private async Task WriteSoapResponseAsync(HttpContext context, string serviceName, string actionName, string output)
    {
        string serviceNamespace = $"urn:schemas-upnp-org:service:{serviceName}:1";
        await context.Response.WriteAsync($"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:u=\"{serviceNamespace}\"><s:Body><u:{actionName}>{output}</u:{actionName}></s:Body></s:Envelope>");
    }

    /// <summary>Identifies requests addressed to the second loopback player.</summary>
    /// <param name="context">The current player request.</param>
    /// <returns>True for the second player's listener.</returns>
    private bool IsSecond(HttpContext context) => context.Connection.LocalIpAddress?.ToString() == "127.0.0.3";

    /// <summary>Counts a source request globally and against its local listener address.</summary>
    /// <param name="context">The incoming request.</param>
    /// <param name="source">The bounded source name.</param>
    private void CountDiagnostic(HttpContext context, string source)
    {
        this._diagnosticRequests.AddOrUpdate(source, 1, static (_, count) => count + 1);
        string address = context.Connection.LocalIpAddress?.ToString() ?? "unknown";
        string key = address + ":" + source;
        this._diagnosticRequests.AddOrUpdate(key, 1, static (_, count) => count + 1);
    }
}
