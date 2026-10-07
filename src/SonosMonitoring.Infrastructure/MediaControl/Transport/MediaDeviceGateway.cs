using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SonosMonitoring.Application.Collection;
using SonosMonitoring.Application.MediaControl.Transport;
using SonosMonitoring.Infrastructure.Collection;
using SonosMonitoring.Infrastructure.Collection.Configuration;

namespace SonosMonitoring.Infrastructure.MediaControl.Transport;

/// <summary>Implements local media reads and explicitly prepared one-attempt SOAP write steps.</summary>
public sealed class MediaDeviceGateway : IMediaDeviceGateway, IDisposable
{
    private readonly MediaSoapClient _soap;
    private readonly MediaTargetVerifier _verifier;
    private readonly ConcurrentDictionary<string, MediaReference> _references = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, GroupVolumeObservation> _groupVolumes = new(StringComparer.Ordinal);

    /// <summary>Uses the live collector configuration and snapshot in the production API host.</summary>
    /// <param name="runtime">Current collector generation.</param>
    public MediaDeviceGateway(CollectorRuntime runtime) : this(runtime, runtime.GetConfiguration().Options,
        null)
    {
    }

    /// <summary>Creates an isolated gateway with a test handler and explicit configuration.</summary>
    /// <param name="reader">Detached collector observations.</param>
    /// <param name="options">Current test configuration.</param>
    /// <param name="handler">Injected HTTP handler; null uses production transport.</param>
    public MediaDeviceGateway(ICollectorSnapshotReader reader, CollectorOptions options, HttpMessageHandler? handler)
    {
        Func<CollectorOptions> currentOptions = reader is CollectorRuntime runtime ?
            () => runtime.GetConfiguration().Options : () => options;
        this._soap = handler is null ? new MediaSoapClient(currentOptions) : new MediaSoapClient(currentOptions, handler);
        this._verifier = new MediaTargetVerifier(reader, currentOptions, this._soap);
    }

    /// <inheritdoc />
    public async Task<MediaPlayback> ReadAsync(string deviceKey, CancellationToken cancellationToken)
    {
        try
        {
            return await this.ReadCoreAsync(deviceKey, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == false)
        {
            throw new MediaDeviceException("device_timeout");
        }
        catch (Exception exception) when (MediaDeviceGateway.IsDeviceFailure(exception))
        {
            throw new MediaDeviceException("device_unavailable");
        }
    }

    /// <summary>Reads validated local state; the public boundary maps protocol failures to safe codes.</summary>
    /// <param name="deviceKey">Selected player UDN.</param>
    /// <param name="cancellationToken">Read cancellation.</param>
    /// <returns>Fresh media observation.</returns>
    private async Task<MediaPlayback> ReadCoreAsync(string deviceKey, CancellationToken cancellationToken)
    {
        ValidatedMediaTarget target = await this._verifier.ValidateAsync(deviceKey, cancellationToken);
        this.Require(target.CoordinatorServices, "AVTransport");
        this.Require(target.Services, "RenderingControl");
        string coordinator = target.CoordinatorAddress;
        IReadOnlySet<string> avContract = await this.OfferedActionsAsync(coordinator, "AVTransport", cancellationToken);
        IReadOnlySet<string> renderingContract = await this.OfferedActionsAsync(target.SelectedAddress,
            "RenderingControl", cancellationToken);
        if (avContract.Contains("GetTransportInfo") == false || avContract.Contains("GetMediaInfo") == false)
        {
            throw new MediaDeviceException("service_unavailable");
        }
        IReadOnlyDictionary<string, string> instance = MediaDeviceGateway.Instance();
        IReadOnlyDictionary<string, string> transport = await this._soap.CallAsync(coordinator, "AVTransport", "GetTransportInfo", instance, cancellationToken);
        IReadOnlyDictionary<string, string> media = await this._soap.CallAsync(coordinator, "AVTransport", "GetMediaInfo", instance, cancellationToken);
        IReadOnlyDictionary<string, string> position = await this.OptionalReadAsync(coordinator, "AVTransport", "GetPositionInfo",
            avContract, instance, cancellationToken);
        IReadOnlyDictionary<string, string> settings = await this.OptionalReadAsync(coordinator, "AVTransport", "GetTransportSettings",
            avContract, instance, cancellationToken);
        IReadOnlyDictionary<string, string> crossfade = await this.OptionalReadAsync(coordinator, "AVTransport", "GetCrossfadeMode",
            avContract, instance, cancellationToken);
        IReadOnlyDictionary<string, string> actions = await this.OptionalReadAsync(coordinator, "AVTransport", "GetCurrentTransportActions",
            avContract, instance, cancellationToken);
        IReadOnlyDictionary<string, string> volume = await this.OptionalReadAsync(target.SelectedAddress, "RenderingControl", "GetVolume",
            renderingContract, MediaDeviceGateway.Args(("InstanceID", "0"), ("Channel", "Master")), cancellationToken);
        IReadOnlyDictionary<string, string> mute = await this.OptionalReadAsync(target.SelectedAddress, "RenderingControl", "GetMute",
            renderingContract, MediaDeviceGateway.Args(("InstanceID", "0"), ("Channel", "Master")), cancellationToken);
        bool? outputFixed = renderingContract.Contains("GetOutputFixed") ?
            await this.ReadOutputFixedAsync(target.SelectedAddress, cancellationToken) : null;
        string groupKey = MediaDeviceGateway.GroupKey(target.Context);
        int? groupVolume = this._groupVolumes.TryGetValue(groupKey, out GroupVolumeObservation? observed) &&
            observed.ExpiresUtc > DateTimeOffset.UtcNow ? observed.Volume : null;
        bool? groupMute = null;
        IReadOnlySet<string> groupContract = new HashSet<string>(StringComparer.Ordinal);
        if (target.CoordinatorServices.Contains("GroupRenderingControl"))
        {
            groupContract = await this.OfferedActionsAsync(coordinator, "GroupRenderingControl", cancellationToken);
            if (groupContract.Contains("GetGroupMute"))
            {
                // GetGroupVolume depends on SnapshotGroupVolume, which changes technical device state.
                // Pure reads expose unknown group volume until an explicitly claimed snapshot step is sent.
                IReadOnlyDictionary<string, string> groupMuteFields = await this.OptionalReadAsync(coordinator,
                    "GroupRenderingControl", "GetGroupMute", groupContract, instance, cancellationToken);
                groupMute = MediaDeviceGateway.Bool(groupMuteFields, "CurrentMute");
            }
        }

        string uri = media.GetValueOrDefault("CurrentURI") ?? string.Empty;
        if (uri.Length > 4096)
        {
            throw new FormatException("Media source identifier exceeds the limit.");
        }
        string token = MediaDeviceGateway.SourceToken(uri);
        MediaTargetContext context = target.Context with { SourceToken = token };
        string source = MediaDeviceGateway.SourceKind(uri);
        string? metadata = position.GetValueOrDefault("TrackMetaData");
        (string? title, string? artist, string? album) = MediaDeviceGateway.ReadMetadata(metadata);
        double? duration = MediaDeviceGateway.Duration(position.GetValueOrDefault("TrackDuration"));
        double? positionSeconds = MediaDeviceGateway.Duration(position.GetValueOrDefault("RelTime"));
        string[] current = (actions.GetValueOrDefault("Actions") ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Split('_')[^1]).ToArray();
        List<string> available = [];
        if (renderingContract.Contains("SetMute"))
        {
            available.AddRange(["mute", "unmute"]);
        }
        if (outputFixed == false && renderingContract.Contains("SetVolume"))
        {
            available.Add("volume");
        }
        if (avContract.Contains("Play") && current.Contains("Play", StringComparer.OrdinalIgnoreCase))
        {
            available.Add("resume");
        }
        if (avContract.Contains("Pause") && current.Contains("Pause", StringComparer.OrdinalIgnoreCase))
        {
            available.Add("pause");
        }
        if (avContract.Contains("Stop") && current.Contains("Stop", StringComparer.OrdinalIgnoreCase))
        {
            available.Add("stop");
        }
        if (source == "queue" && avContract.Contains("Next") && current.Contains("Next", StringComparer.OrdinalIgnoreCase))
        {
            available.Add("next");
        }
        if (source == "queue" && avContract.Contains("Previous") && current.Contains("Previous", StringComparer.OrdinalIgnoreCase))
        {
            available.Add("previous");
        }
        if (duration is not null && avContract.Contains("Seek") && current.Contains("SeekTime", StringComparer.OrdinalIgnoreCase))
        {
            available.Add("seek");
        }
        if (source == "queue")
        {
            if (avContract.Contains("SetPlayMode"))
            {
                available.Add("mode");
            }
            if (avContract.Contains("SetCrossfadeMode") && MediaDeviceGateway.Bool(crossfade, "CrossfadeMode") is not null)
            {
                available.Add("crossfade");
            }
        }
        if (target.CoordinatorServices.Contains("ContentDirectory"))
        {
            IReadOnlySet<string> contentContract = await this.OfferedActionsAsync(coordinator, "ContentDirectory", cancellationToken);
            if (contentContract.Contains("Browse"))
            {
                if (avContract.Contains("SetAVTransportURI") && avContract.Contains("Play"))
                {
                    available.AddRange(["start-favorite", "start-stream"]);
                }
                if (avContract.Contains("SetAVTransportURI") && avContract.Contains("Seek") && avContract.Contains("Play"))
                {
                    available.Add("queue-play");
                }
                if (avContract.Contains("AddURIToQueue"))
                {
                    available.Add("queue-append");
                }
                if (avContract.Contains("RemoveTrackRangeFromQueue"))
                {
                    available.Add("queue-remove");
                }
                if (avContract.Contains("ReorderTracksInQueue"))
                {
                    available.Add("queue-reorder");
                }
                if (avContract.Contains("RemoveAllTracksFromQueue"))
                {
                    available.Add("queue-clear");
                }
            }
        }
        IReadOnlySet<string> selectedAvContract = target.SelectedAddress == coordinator ? avContract :
            target.Services.Contains("AVTransport") ? await this.OfferedActionsAsync(target.SelectedAddress, "AVTransport", cancellationToken) :
            new HashSet<string>(StringComparer.Ordinal);
        if (target.HasBondedMembers == false && target.Context.MemberDeviceKeys.Count == 1 &&
            selectedAvContract.Contains("SetAVTransportURI"))
        {
            available.Add("join");
        }
        if (target.HasBondedMembers == false && target.Context.MemberDeviceKeys.Count > 1 &&
            target.Context.DeviceKey != target.Context.CoordinatorDeviceKey &&
            selectedAvContract.Contains("BecomeCoordinatorOfStandaloneGroup"))
        {
            available.Add("leave");
        }
        if (target.CoordinatorServices.Contains("GroupRenderingControl"))
        {
            if (groupContract.Contains("SnapshotGroupVolume") && groupContract.Contains("SetGroupVolume"))
            {
                available.Add("group-volume");
            }
            if (groupContract.Contains("SetGroupMute") && groupContract.Contains("GetGroupMute"))
            {
                available.AddRange(["group-mute", "group-unmute"]);
            }
        }
        return new MediaPlayback(context, DateTimeOffset.UtcNow, source,
            transport.GetValueOrDefault("CurrentTransportState") ?? "UNKNOWN",
            MediaDeviceGateway.Int(volume, "CurrentVolume"), MediaDeviceGateway.Bool(mute, "CurrentMute"),
            groupVolume, groupMute, title, artist, album,
            duration, positionSeconds, available,
            MediaDeviceGateway.SemanticMode(settings.GetValueOrDefault("PlayMode")),
            MediaDeviceGateway.Bool(crossfade, "CrossfadeMode"));
    }

    /// <inheritdoc />
    public async Task<MediaPage> BrowseAsync(string deviceKey, string kind, int offset, int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            return await this.BrowseCoreAsync(deviceKey, kind, offset, limit, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == false)
        {
            throw new MediaDeviceException("device_timeout");
        }
        catch (Exception exception) when (MediaDeviceGateway.IsDeviceFailure(exception))
        {
            throw new MediaDeviceException("device_unavailable");
        }
    }

    /// <summary>Browses a validated page before hiding its private media data in opaque references.</summary>
    /// <param name="deviceKey">Selected UDN.</param>
    /// <param name="kind">Queue or favorites.</param>
    /// <param name="offset">Zero-based offset.</param>
    /// <param name="limit">Page limit.</param>
    /// <param name="cancellationToken">Read cancellation.</param>
    /// <returns>Safe display page.</returns>
    private async Task<MediaPage> BrowseCoreAsync(string deviceKey, string kind, int offset, int limit,
        CancellationToken cancellationToken)
    {
        if (kind is not ("queue" or "favorites") || offset < 0 || limit is < 1 or > 100 || offset > 10_000)
        {
            throw new ArgumentException("Invalid media browse request.");
        }
        ValidatedMediaTarget target = await this._verifier.ValidateAsync(deviceKey, cancellationToken);
        this.Require(target.CoordinatorServices, "ContentDirectory");
        string address = target.CoordinatorAddress;
        await this.RequireActionAsync(address, "ContentDirectory", "Browse", cancellationToken);
        string objectId = kind == "queue" ? "Q:0" : "FV:2";
        IReadOnlyDictionary<string, string> fields = await this._soap.CallAsync(address, "ContentDirectory", "Browse",
            MediaDeviceGateway.Args(("ObjectID", objectId), ("BrowseFlag", "BrowseDirectChildren"),
                ("Filter", "*"), ("StartingIndex", offset.ToString(CultureInfo.InvariantCulture)),
                ("RequestedCount", limit.ToString(CultureInfo.InvariantCulture)), ("SortCriteria", "")), cancellationToken);
        int count = MediaDeviceGateway.RequiredInt(fields, "NumberReturned", 0, limit);
        int total = MediaDeviceGateway.RequiredInt(fields, "TotalMatches", 0, 100_000);
        string revision = fields.GetValueOrDefault("UpdateID") ?? throw new FormatException("Missing browse revision.");
        if (uint.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out _) == false)
        {
            throw new FormatException("Invalid browse revision.");
        }
        IReadOnlyList<MediaBrowseEntry> entries = MediaBrowseParser.Parse(fields.GetValueOrDefault("Result") ?? string.Empty,
            count, limit);
        List<MediaItem> items = [];
        foreach ((MediaBrowseEntry entry, int index) in entries.Select((entry, index) => (entry, index)))
        {
            string reference = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            this._references[reference] = new MediaReference(deviceKey, target.Context.HouseholdId, kind,
                entry, revision, kind == "queue" ? offset + index + 1 : null, DateTimeOffset.UtcNow.AddMinutes(5));
            items.Add(new MediaItem(reference, entry.Title, entry.Artist, entry.Album,
                kind == "queue" ? offset + index + 1 : null));
        }
        this.TrimReferences();
        return new MediaPage(items, offset, total, revision);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaWriteStep>> PrepareAsync(string deviceKey, string action,
        MediaTargetContext context, MediaWriteInputs inputs, CancellationToken cancellationToken)
    {
        try
        {
            return await this.PrepareCoreAsync(deviceKey, action, context, inputs, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == false)
        {
            throw new MediaDeviceException("device_timeout");
        }
        catch (Exception exception) when (MediaDeviceGateway.IsDeviceFailure(exception))
        {
            throw new MediaDeviceException("device_unavailable");
        }
    }

    /// <summary>Prepares action-specific immutable steps from current device state.</summary>
    /// <param name="deviceKey">Selected UDN.</param>
    /// <param name="action">Named intention.</param>
    /// <param name="context">Displayed preconditions.</param>
    /// <param name="inputs">Action inputs.</param>
    /// <param name="cancellationToken">Read cancellation.</param>
    /// <returns>Prepared steps without write side effects.</returns>
    private async Task<IReadOnlyList<MediaWriteStep>> PrepareCoreAsync(string deviceKey, string action,
        MediaTargetContext context, MediaWriteInputs inputs, CancellationToken cancellationToken)
    {
        if (deviceKey != context.DeviceKey)
        {
            throw new MediaDeviceException("target_changed");
        }
        MediaPlayback current = await this.ReadAsync(deviceKey, cancellationToken);
        MediaDeviceGateway.MatchContext(context, current.Context, true);
        if (current.AvailableActions.Contains(action, StringComparer.Ordinal) == false)
        {
            throw new MediaDeviceException("action_unavailable");
        }
        ValidatedMediaTarget target = await this._verifier.ValidateAsync(deviceKey, cancellationToken);
        List<MediaWriteStep> steps = [];
        string address = target.CoordinatorAddress;
        string targetKey = context.CoordinatorDeviceKey;
        string service = "AVTransport";
        string protocolAction;
        IReadOnlyDictionary<string, string> arguments = MediaDeviceGateway.Instance();
        switch (action)
        {
            case "resume":
                protocolAction = "Play";
                arguments = MediaDeviceGateway.Args(("InstanceID", "0"), ("Speed", "1"));
                break;
            case "pause":
                protocolAction = "Pause";
                break;
            case "stop":
                protocolAction = "Stop";
                break;
            case "next":
                protocolAction = "Next";
                break;
            case "previous":
                protocolAction = "Previous";
                break;
            case "seek":
                protocolAction = "Seek";
                if (inputs.PositionSeconds is not double seconds || double.IsFinite(seconds) == false || seconds < 0 ||
                    seconds > 86_399 || current.DurationSeconds is null || seconds > current.DurationSeconds)
                {
                    throw new MediaDeviceException("invalid_position");
                }
                arguments = MediaDeviceGateway.Args(("InstanceID", "0"), ("Unit", "REL_TIME"),
                    ("Target", TimeSpan.FromSeconds(Math.Floor(seconds)).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)));
                break;
            case "volume":
            case "mute":
            case "unmute":
                service = "RenderingControl";
                address = target.SelectedAddress;
                targetKey = deviceKey;
                protocolAction = action == "volume" ? "SetVolume" : "SetMute";
                if (action == "volume")
                {
                    if (current.AvailableActions.Contains("volume", StringComparer.Ordinal) == false)
                    {
                        throw new MediaDeviceException("fixed_output");
                    }
                    arguments = MediaDeviceGateway.Args(("InstanceID", "0"), ("Channel", "Master"),
                        ("DesiredVolume", MediaDeviceGateway.VolumeValue(inputs.Value)));
                }
                else
                {
                    arguments = MediaDeviceGateway.Args(("InstanceID", "0"), ("Channel", "Master"),
                        ("DesiredMute", action == "mute" ? "1" : "0"));
                }
                break;
            case "mode":
                protocolAction = "SetPlayMode";
                if (current.Source != "queue")
                {
                    throw new MediaDeviceException("unsupported_mode");
                }
                arguments = MediaDeviceGateway.Args(("InstanceID", "0"), ("NewPlayMode", MediaDeviceGateway.ProtocolMode(inputs.Mode)));
                break;
            case "crossfade":
                protocolAction = "SetCrossfadeMode";
                if (inputs.Value is not (0 or 1))
                {
                    throw new MediaDeviceException("invalid_crossfade");
                }
                arguments = MediaDeviceGateway.Args(("InstanceID", "0"), ("CrossfadeMode", inputs.Value.Value.ToString(CultureInfo.InvariantCulture)));
                break;
            case "group-volume":
            case "group-mute":
            case "group-unmute":
                service = "GroupRenderingControl";
                protocolAction = action == "group-volume" ? "SetGroupVolume" : "SetGroupMute";
                this.Require(target.CoordinatorServices, service);
                if (action == "group-volume")
                {
                    steps.Add(this.Step(targetKey, address, service, "SnapshotGroupVolume", MediaDeviceGateway.Instance(), context));
                    arguments = MediaDeviceGateway.Args(("InstanceID", "0"), ("DesiredVolume", MediaDeviceGateway.VolumeValue(inputs.Value)));
                }
                else
                {
                    arguments = MediaDeviceGateway.Args(("InstanceID", "0"), ("DesiredMute", action == "group-mute" ? "1" : "0"));
                }
                break;
            case "start-favorite":
            case "start-stream":
            case "queue-append":
                MediaReference reference = this.ResolveReference(inputs.Reference, deviceKey, context.HouseholdId,
                    action == "start-favorite" ? "favorites" : null);
                if (reference.Entry.Uri is null)
                {
                    throw new MediaDeviceException("media_unplayable");
                }
                if (action == "queue-append")
                {
                    await this.CheckQueueRevisionAsync(deviceKey, context.QueueRevision, cancellationToken);
                    protocolAction = "AddURIToQueue";
                    arguments = MediaDeviceGateway.Args(("InstanceID", "0"), ("EnqueuedURI", reference.Entry.Uri),
                        ("EnqueuedURIMetaData", reference.Entry.Metadata), ("DesiredFirstTrackNumberEnqueued", "0"),
                        ("EnqueueAsNext", "0"));
                }
                else
                {
                    protocolAction = "SetAVTransportURI";
                    arguments = MediaDeviceGateway.Args(("InstanceID", "0"), ("CurrentURI", reference.Entry.Uri),
                        ("CurrentURIMetaData", reference.Entry.Metadata));
                    steps.Add(this.Step(targetKey, address, service, protocolAction, arguments, context));
                    MediaWriteStep play = this.Step(targetKey, address, service, "Play",
                        MediaDeviceGateway.Args(("InstanceID", "0"), ("Speed", "1")), context) with
                    { ExpectedSourceToken = MediaDeviceGateway.SourceToken(reference.Entry.Uri) };
                    steps.Add(play);
                    await this.RequireStepsAsync(steps, cancellationToken);
                    return steps;
                }
                break;
            case "queue-play":
                await this.CheckQueueRevisionAsync(deviceKey, context.QueueRevision, cancellationToken);
                MediaReference queueEntry = this.ResolveReference(inputs.Reference, deviceKey, context.HouseholdId, "queue");
                if (queueEntry.Revision != context.QueueRevision || queueEntry.Index is null)
                {
                    throw new MediaDeviceException("queue_changed");
                }
                int queueIndex = queueEntry.Index.Value;
                string queueUri = $"x-rincon-queue:{targetKey}#0";
                steps.Add(this.Step(targetKey, address, service, "SetAVTransportURI",
                    MediaDeviceGateway.Args(("InstanceID", "0"), ("CurrentURI", queueUri), ("CurrentURIMetaData", "")), context) with
                { RequiredQueueRevision = context.QueueRevision });
                string queueToken = MediaDeviceGateway.SourceToken(queueUri);
                steps.Add(this.Step(targetKey, address, service, "Seek",
                    MediaDeviceGateway.Args(("InstanceID", "0"), ("Unit", "TRACK_NR"),
                        ("Target", queueIndex.ToString(CultureInfo.InvariantCulture))), context) with
                { ExpectedSourceToken = queueToken, RequiredQueueRevision = context.QueueRevision });
                steps.Add(this.Step(targetKey, address, service, "Play",
                    MediaDeviceGateway.Args(("InstanceID", "0"), ("Speed", "1")), context) with
                { ExpectedSourceToken = queueToken, RequiredQueueRevision = context.QueueRevision });
                await this.RequireStepsAsync(steps, cancellationToken);
                return steps;
            case "queue-remove":
            case "queue-reorder":
            case "queue-clear":
                string revision = await this.CheckQueueRevisionAsync(deviceKey, context.QueueRevision, cancellationToken);
                protocolAction = action switch
                {
                    "queue-remove" => "RemoveTrackRangeFromQueue",
                    "queue-reorder" => "ReorderTracksInQueue",
                    _ => "RemoveAllTracksFromQueue"
                };
                if (action == "queue-remove")
                {
                    arguments = MediaDeviceGateway.Args(("InstanceID", "0"), ("UpdateID", revision),
                        ("StartingIndex", MediaDeviceGateway.PositiveIndex(inputs.Index).ToString(CultureInfo.InvariantCulture)),
                        ("NumberOfTracks", "1"));
                }
                if (action == "queue-reorder")
                {
                    arguments = MediaDeviceGateway.Args(("InstanceID", "0"),
                        ("StartingIndex", MediaDeviceGateway.PositiveIndex(inputs.Index).ToString(CultureInfo.InvariantCulture)),
                        ("NumberOfTracks", "1"),
                        ("InsertBefore", MediaDeviceGateway.PositiveIndex(inputs.NewIndex).ToString(CultureInfo.InvariantCulture)),
                        ("UpdateID", revision));
                }
                break;
            case "join":
                if (inputs.Reference is null || inputs.Reference == deviceKey || context.MemberDeviceKeys.Count != 1 ||
                    target.HasBondedMembers ||
                    inputs.DestinationContext is null)
                {
                    throw new MediaDeviceException("invalid_group_target");
                }
                MediaPlayback destination = await this.ReadAsync(inputs.Reference, cancellationToken);
                MediaDeviceGateway.MatchContext(inputs.DestinationContext, destination.Context, true);
                ValidatedMediaTarget destinationTarget = await this._verifier.ValidateAsync(inputs.Reference, cancellationToken);
                if (destination.Context.HouseholdId != context.HouseholdId || destination.Context.CoordinatorDeviceKey != inputs.Reference ||
                    destinationTarget.HasBondedMembers)
                {
                    throw new MediaDeviceException("invalid_group_target");
                }
                protocolAction = "SetAVTransportURI";
                address = target.SelectedAddress;
                targetKey = deviceKey;
                arguments = MediaDeviceGateway.Args(("InstanceID", "0"), ("CurrentURI", $"x-rincon:{inputs.Reference}"),
                    ("CurrentURIMetaData", ""));
                break;
            case "leave":
                if (context.DeviceKey == context.CoordinatorDeviceKey || context.MemberDeviceKeys.Count <= 1 ||
                    target.HasBondedMembers)
                {
                    throw new MediaDeviceException("invalid_group_target");
                }
                protocolAction = "BecomeCoordinatorOfStandaloneGroup";
                address = target.SelectedAddress;
                targetKey = deviceKey;
                break;
            default:
                throw new MediaDeviceException("unsupported_action");
        }
        this.Require(targetKey == deviceKey ? target.Services : target.CoordinatorServices, service);
        MediaWriteStep finalStep = this.Step(targetKey, address, service, protocolAction, arguments, context);
        if (action == "join")
        {
            finalStep = finalStep with { DestinationContext = inputs.DestinationContext };
        }
        if (action.StartsWith("queue-", StringComparison.Ordinal))
        {
            finalStep = finalStep with { RequiredQueueRevision = context.QueueRevision };
        }
        steps.Add(finalStep);
        await this.RequireStepsAsync(steps, cancellationToken);
        return steps;
    }

    /// <inheritdoc />
    public async Task SendAsync(MediaWriteStep step, CancellationToken cancellationToken)
    {
        if (MediaSoapProtocol.IsAllowed(step.Service, step.Action) == false ||
            MediaDeviceGateway.IsWriteAction(step.Service, step.Action) == false)
        {
            throw new MediaDeviceException("unsupported_action");
        }
        try
        {
            ValidatedMediaTarget target = await this._verifier.ValidateAsync(step.Context.DeviceKey, cancellationToken);
            MediaDeviceGateway.MatchContext(step.Context, target.Context, false);
            if (target.HasBondedMembers && (step.Action == "BecomeCoordinatorOfStandaloneGroup" ||
                step.DestinationContext is not null))
            {
                throw new MediaDeviceException("bonded_group_unsupported");
            }
            string address = step.TargetDeviceKey == step.Context.DeviceKey ? target.SelectedAddress :
                step.TargetDeviceKey == step.Context.CoordinatorDeviceKey ? target.CoordinatorAddress :
                throw new MediaDeviceException("target_changed");
            if (address != step.TargetAddress)
            {
                throw new MediaDeviceException("target_changed");
            }
            this.Require(step.TargetDeviceKey == step.Context.DeviceKey ? target.Services : target.CoordinatorServices, step.Service);
            await this.RequireActionAsync(address, step.Service, step.Action, cancellationToken);
            if (step.DestinationContext is not null)
            {
                MediaPlayback destination = await this.ReadAsync(step.DestinationContext.DeviceKey, cancellationToken);
                MediaDeviceGateway.MatchContext(step.DestinationContext, destination.Context, true);
                ValidatedMediaTarget destinationTarget = await this._verifier.ValidateAsync(
                    step.DestinationContext.DeviceKey, cancellationToken);
                if (destinationTarget.HasBondedMembers)
                {
                    throw new MediaDeviceException("bonded_group_unsupported");
                }
            }
            if (step.RequiredQueueRevision is not null)
            {
                await this.CheckQueueRevisionAsync(step.Context.DeviceKey, step.RequiredQueueRevision, cancellationToken);
            }
            if (step.Service == "RenderingControl" && step.Action == "SetVolume" &&
                await this.ReadOutputFixedAsync(address, cancellationToken) != false)
            {
                throw new MediaDeviceException("fixed_output");
            }
            if (step.ExpectedSourceToken is not null || step.Context.SourceToken.Length > 0)
            {
                IReadOnlyDictionary<string, string> source = await this._soap.CallAsync(target.CoordinatorAddress,
                    "AVTransport", "GetMediaInfo", MediaDeviceGateway.Instance(), cancellationToken);
                string actual = MediaDeviceGateway.SourceToken(source.GetValueOrDefault("CurrentURI") ?? string.Empty);
                if (actual != (step.ExpectedSourceToken ?? step.Context.SourceToken))
                {
                    throw new MediaDeviceException("source_changed");
                }
            }
            if ((step.Service == "GroupRenderingControl" && step.Action is ("SnapshotGroupVolume" or "SetGroupVolume")) ||
                (step.Service == "RenderingControl" && step.Action == "SetVolume"))
            {
                this._groupVolumes.TryRemove(MediaDeviceGateway.GroupKey(step.Context), out _);
            }
            // The durable worker has already claimed this step. The transport makes exactly one write call.
            await this._soap.CallAsync(address, step.Service, step.Action, step.Arguments, cancellationToken);
        }
        catch (MediaSoapFaultException fault)
        {
            throw new MediaDeviceException("soap_fault_" + fault.Code, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == false)
        {
            throw new MediaDeviceException("device_timeout");
        }
        catch (Exception exception) when (MediaDeviceGateway.IsDeviceFailure(exception))
        {
            throw new MediaDeviceException("device_unavailable");
        }
    }

    /// <inheritdoc />
    public async Task<bool?> ConfirmAsync(string action, MediaTargetContext context, MediaWriteInputs inputs,
        CancellationToken cancellationToken)
    {
        try
        {
            return await this.ConfirmCoreAsync(action, context, inputs, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == false)
        {
            throw new MediaDeviceException("device_timeout");
        }
        catch (Exception exception) when (MediaDeviceGateway.IsDeviceFailure(exception))
        {
            throw new MediaDeviceException("device_unavailable");
        }
    }

    /// <summary>Compares a fresh read with an observable desired state.</summary>
    /// <param name="action">Original named intention.</param>
    /// <param name="context">Original target context.</param>
    /// <param name="inputs">Original values.</param>
    /// <param name="cancellationToken">Read cancellation.</param>
    /// <returns>Match, mismatch, or null when unobservable.</returns>
    private async Task<bool?> ConfirmCoreAsync(string action, MediaTargetContext context, MediaWriteInputs inputs,
        CancellationToken cancellationToken)
    {
        MediaPlayback playback = await this.ReadAsync(context.DeviceKey, cancellationToken);
        if (action == "group-volume" && inputs.Value is >= 0 and <= 100)
        {
            MediaDeviceGateway.MatchContext(context, playback.Context, false);
            ValidatedMediaTarget target = await this._verifier.ValidateAsync(context.DeviceKey, cancellationToken);
            this.Require(target.CoordinatorServices, "GroupRenderingControl");
            IReadOnlySet<string> offered = await this.OfferedActionsAsync(target.CoordinatorAddress,
                "GroupRenderingControl", cancellationToken);
            if (offered.Contains("GetGroupVolume") == false)
            {
                return null;
            }
            IReadOnlyDictionary<string, string> fields = await this._soap.CallAsync(target.CoordinatorAddress,
                "GroupRenderingControl", "GetGroupVolume", MediaDeviceGateway.Instance(), cancellationToken);
            int? actual = MediaDeviceGateway.Int(fields, "CurrentVolume");
            if (actual is not null)
            {
                this._groupVolumes[MediaDeviceGateway.GroupKey(target.Context)] =
                    new GroupVolumeObservation(actual.Value, DateTimeOffset.UtcNow.AddSeconds(5));
                if (this._groupVolumes.Count > 512)
                {
                    foreach ((string key, GroupVolumeObservation value) in this._groupVolumes)
                    {
                        if (value.ExpiresUtc <= DateTimeOffset.UtcNow || this._groupVolumes.Count > 512)
                        {
                            this._groupVolumes.TryRemove(key, out _);
                        }
                    }
                }
            }
            return actual is null ? null : actual == inputs.Value;
        }
        return action switch
        {
            "resume" => playback.TransportState == "PLAYING",
            "pause" => playback.TransportState == "PAUSED_PLAYBACK",
            "stop" => playback.TransportState == "STOPPED",
            "volume" when playback.Volume is not null => playback.Volume == inputs.Value,
            "mute" when playback.Muted is not null => playback.Muted == true,
            "unmute" when playback.Muted is not null => playback.Muted == false,
            "mode" when playback.PlayMode is not null => playback.PlayMode == inputs.Mode,
            "crossfade" when playback.Crossfade is not null => playback.Crossfade == (inputs.Value == 1),
            "group-mute" when playback.GroupMuted is not null => playback.GroupMuted == true,
            "group-unmute" when playback.GroupMuted is not null => playback.GroupMuted == false,
            "join" when inputs.Reference is not null => playback.Context.CoordinatorDeviceKey == inputs.Reference,
            "leave" => playback.Context.CoordinatorDeviceKey == context.DeviceKey && playback.Context.MemberDeviceKeys.Count == 1,
            _ => null
        };
    }

    /// <summary>Closes the local connection pool.</summary>
    public void Dispose() => this._soap.Dispose();

    /// <summary>Builds a detached step with the original source precondition.</summary>
    /// <param name="key">Stable target UDN.</param>
    /// <param name="address">Approved address.</param>
    /// <param name="service">Whitelisted service.</param>
    /// <param name="action">Whitelisted action.</param>
    /// <param name="arguments">Protocol arguments.</param>
    /// <param name="context">Original preconditions.</param>
    /// <returns>Immutable step.</returns>
    private MediaWriteStep Step(string key, string address, string service, string action,
        IReadOnlyDictionary<string, string> arguments, MediaTargetContext context) =>
        new(key, address, service, action, arguments, context);

    /// <summary>Requires a service/control pair advertised by the freshly checked description.</summary>
    /// <param name="services">Advertised pairs.</param>
    /// <param name="name">Required service.</param>
    private void Require(IReadOnlySet<string> services, string name)
    {
        if (services.Contains(name) == false)
        {
            throw new MediaDeviceException("service_unavailable");
        }
    }

    /// <summary>Reads the exact current action names advertised by a fixed local SCPD path.</summary>
    /// <param name="address">Validated literal address.</param>
    /// <param name="service">Whitelisted service.</param>
    /// <param name="cancellationToken">Read cancellation.</param>
    /// <returns>Advertised action names.</returns>
    private async Task<IReadOnlySet<string>> OfferedActionsAsync(string address, string service,
        CancellationToken cancellationToken)
    {
        string xml = await this._soap.ServiceDescriptionAsync(address, service, cancellationToken);
        return MediaServiceContractParser.ParseActions(xml);
    }

    /// <summary>Returns unknown fields when an optional advertised read is absent or explicitly unsupported.</summary>
    /// <param name="address">Validated literal address.</param>
    /// <param name="service">Whitelisted service.</param>
    /// <param name="action">Optional read action.</param>
    /// <param name="offered">Current SCPD actions.</param>
    /// <param name="arguments">Fixed read arguments.</param>
    /// <param name="cancellationToken">Read cancellation.</param>
    /// <returns>Output fields or an empty set.</returns>
    private async Task<IReadOnlyDictionary<string, string>> OptionalReadAsync(string address, string service,
        string action, IReadOnlySet<string> offered, IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken)
    {
        if (offered.Contains(action) == false)
        {
            return new Dictionary<string, string>();
        }
        try
        {
            return await this._soap.CallAsync(address, service, action, arguments, cancellationToken);
        }
        catch (MediaSoapFaultException)
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>Requires one action in the current local service description.</summary>
    /// <param name="address">Validated literal address.</param>
    /// <param name="service">Whitelisted service.</param>
    /// <param name="action">Whitelisted action.</param>
    /// <param name="cancellationToken">Read cancellation.</param>
    private async Task RequireActionAsync(string address, string service, string action, CancellationToken cancellationToken)
    {
        IReadOnlySet<string> offered = await this.OfferedActionsAsync(address, service, cancellationToken);
        if (offered.Contains(action) == false)
        {
            throw new MediaDeviceException("action_unavailable");
        }
    }

    /// <summary>Proves every planned technical write step is currently advertised.</summary>
    /// <param name="steps">Prepared steps.</param>
    /// <param name="cancellationToken">Read cancellation.</param>
    private async Task RequireStepsAsync(IReadOnlyList<MediaWriteStep> steps, CancellationToken cancellationToken)
    {
        foreach (MediaWriteStep step in steps)
        {
            await this.RequireActionAsync(step.TargetAddress, step.Service, step.Action, cancellationToken);
        }
    }

    /// <summary>Retrieves an unexpired reference bound to the selected player and household.</summary>
    /// <param name="reference">Opaque reference.</param>
    /// <param name="deviceKey">Selected UDN.</param>
    /// <param name="household">Current household.</param>
    /// <param name="kind">Required browse kind, if any.</param>
    /// <returns>Private media data.</returns>
    private MediaReference ResolveReference(string? reference, string deviceKey, string household, string? kind)
    {
        if (reference is null || this._references.TryGetValue(reference, out MediaReference? entry) == false ||
            entry.ExpiresUtc <= DateTimeOffset.UtcNow || entry.DeviceKey != deviceKey || entry.Household != household ||
            (kind is not null && entry.Kind != kind))
        {
            throw new MediaDeviceException("media_reference_expired");
        }
        return entry;
    }

    /// <summary>Checks fixed-output capability without inferring an adjustable amplifier from volume alone.</summary>
    /// <param name="address">Validated player address.</param>
    /// <param name="cancellationToken">Read cancellation.</param>
    /// <returns>Fixed-output state, or null when the optional action is unavailable.</returns>
    private async Task<bool?> ReadOutputFixedAsync(string address, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyDictionary<string, string> fields = await this._soap.CallAsync(address, "RenderingControl",
                "GetOutputFixed", MediaDeviceGateway.Instance(), cancellationToken);
            return MediaDeviceGateway.Bool(fields, "CurrentFixed");
        }
        catch (MediaSoapFaultException)
        {
            return null;
        }
    }

    /// <summary>Checks current queue revision before an index or mutation is prepared.</summary>
    /// <param name="deviceKey">Selected UDN.</param>
    /// <param name="revision">Displayed revision.</param>
    /// <param name="cancellationToken">Read cancellation.</param>
    /// <returns>Current numeric revision.</returns>
    private async Task<string> CheckQueueRevisionAsync(string deviceKey, string? revision, CancellationToken cancellationToken)
    {
        if (revision is null)
        {
            throw new MediaDeviceException("queue_revision_required");
        }
        MediaPage page = await this.BrowseAsync(deviceKey, "queue", 0, 1, cancellationToken);
        if (page.Revision != revision)
        {
            throw new MediaDeviceException("queue_changed");
        }
        return revision;
    }

    /// <summary>Removes expired references and bounds memory if clients abandon pages.</summary>
    private void TrimReferences()
    {
        if (this._references.Count <= 4096)
        {
            return;
        }
        foreach ((string key, MediaReference value) in this._references)
        {
            if (value.ExpiresUtc <= DateTimeOffset.UtcNow || this._references.Count > 4096)
            {
                this._references.TryRemove(key, out _);
            }
        }
    }

    /// <summary>Compares a displayed context with current identity, group, and optionally source.</summary>
    /// <param name="expected">Displayed context.</param>
    /// <param name="actual">Current context.</param>
    /// <param name="source">Whether source must also match.</param>
    private static void MatchContext(MediaTargetContext expected, MediaTargetContext actual, bool source)
    {
        if (expected.DeviceKey != actual.DeviceKey || expected.HouseholdId != actual.HouseholdId ||
            expected.CoordinatorDeviceKey != actual.CoordinatorDeviceKey ||
            expected.MemberDeviceKeys.Order(StringComparer.Ordinal).SequenceEqual(actual.MemberDeviceKeys.Order(StringComparer.Ordinal)) == false ||
            (source && expected.SourceToken != actual.SourceToken))
        {
            throw new MediaDeviceException("context_changed");
        }
    }

    /// <summary>Creates a protocol dictionary in insertion order.</summary>
    /// <param name="values">Known action fields.</param>
    /// <returns>Detached ordered dictionary.</returns>
    private static IReadOnlyDictionary<string, string> Args(params (string Name, string Value)[] values) =>
        values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    /// <summary>Gets instance-zero arguments.</summary>
    /// <returns>Protocol fields.</returns>
    private static IReadOnlyDictionary<string, string> Instance() => MediaDeviceGateway.Args(("InstanceID", "0"));

    /// <summary>Computes an opaque current URI fingerprint.</summary>
    /// <param name="uri">Private current URI.</param>
    /// <returns>Stable SHA-256 token.</returns>
    private static string SourceToken(string uri) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri)));

    /// <summary>Builds a stable cache key for one exact household/group membership.</summary>
    /// <param name="context">Validated group context.</param>
    /// <returns>Opaque source-independent group key.</returns>
    private static string GroupKey(MediaTargetContext context) => context.HouseholdId + "|" +
        context.CoordinatorDeviceKey + "|" + string.Join(",", context.MemberDeviceKeys.Order(StringComparer.Ordinal));

    /// <summary>Classifies known local sources without exposing the URI.</summary>
    /// <param name="uri">Private current URI.</param>
    /// <returns>Bounded source kind.</returns>
    private static string SourceKind(string uri) => uri.StartsWith("x-rincon-queue:", StringComparison.Ordinal) ? "queue" :
        uri.StartsWith("x-sonosapi-stream:", StringComparison.Ordinal) ? "radio" :
        uri.StartsWith("x-sonos-htastream:", StringComparison.Ordinal) ? "tv" :
        uri.StartsWith("x-rincon-stream:", StringComparison.Ordinal) ? "line-in" : "unknown";

    /// <summary>Maps the stable UI mode to an exact Sonos play mode.</summary>
    /// <param name="mode">Requested UI mode.</param>
    /// <returns>Protocol mode.</returns>
    private static string ProtocolMode(string? mode) => mode switch
    {
        "normal" => "NORMAL",
        "repeat-all" => "REPEAT_ALL",
        "repeat-one" => "REPEAT_ONE",
        "shuffle" => "SHUFFLE_NOREPEAT",
        "shuffle-repeat" => "SHUFFLE",
        _ => throw new MediaDeviceException("unsupported_mode")
    };

    /// <summary>Maps a reported Sonos mode to a stable UI mode when known.</summary>
    /// <param name="mode">Reported protocol mode.</param>
    /// <returns>Stable UI mode, or null.</returns>
    private static string? SemanticMode(string? mode) => mode switch
    {
        "NORMAL" => "normal",
        "REPEAT_ALL" => "repeat-all",
        "REPEAT_ONE" => "repeat-one",
        "SHUFFLE_NOREPEAT" => "shuffle",
        "SHUFFLE" => "shuffle-repeat",
        _ => null
    };

    /// <summary>Parses bounded DIDL title fields from the currently reported track.</summary>
    /// <param name="metadata">Escaped DIDL-Lite metadata.</param>
    /// <returns>Title, artist, and album when present.</returns>
    private static (string? Title, string? Artist, string? Album) ReadMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata) || metadata == "NOT_IMPLEMENTED")
        {
            return (null, null, null);
        }
        var document = MediaSoapProtocol.ReadDocument(metadata);
        string? value(string name) => document.Descendants().FirstOrDefault(element => element.Name.LocalName == name)?.Value;
        string? title = value("title");
        string? artist = value("creator");
        string? album = value("album");
        return (title?.Length <= 512 ? title : null, artist?.Length <= 512 ? artist : null,
            album?.Length <= 512 ? album : null);
    }

    /// <summary>Reads a numeric SOAP output only inside supported bounds.</summary>
    /// <param name="fields">Output fields.</param>
    /// <param name="name">Field name.</param>
    /// <param name="minimum">Minimum value.</param>
    /// <param name="maximum">Maximum value.</param>
    /// <returns>Parsed value.</returns>
    private static int RequiredInt(IReadOnlyDictionary<string, string> fields, string name, int minimum, int maximum)
    {
        if (fields.TryGetValue(name, out string? value) == false ||
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) == false ||
            parsed < minimum || parsed > maximum)
        {
            throw new FormatException("Invalid numeric SOAP output.");
        }
        return parsed;
    }

    /// <summary>Reads a bounded optional numeric field.</summary>
    /// <param name="fields">Output fields.</param>
    /// <param name="name">Field name.</param>
    /// <returns>Parsed value or null.</returns>
    private static int? Int(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out string? value) && int.TryParse(value, NumberStyles.None,
            CultureInfo.InvariantCulture, out int parsed) && parsed is >= 0 and <= 100 ? parsed : null;

    /// <summary>Reads a strict UPnP boolean field.</summary>
    /// <param name="fields">Output fields.</param>
    /// <param name="name">Field name.</param>
    /// <returns>Reported boolean or null.</returns>
    private static bool? Bool(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.GetValueOrDefault(name) switch { "1" => true, "0" => false, _ => null };

    /// <summary>Parses a finite HH:MM:SS time, leaving unknown radio duration null.</summary>
    /// <param name="value">Protocol clock.</param>
    /// <returns>Seconds or null.</returns>
    private static double? Duration(string? value)
    {
        string[] parts = value?.Split(':') ?? [];
        if (parts.Length != 3 || parts[0].Length is < 1 or > 2 || parts[1].Length != 2 || parts[2].Length != 2 ||
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int hours) == false ||
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) == false ||
            int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) == false ||
            hours > 23 || minutes > 59 || seconds > 59)
        {
            return null;
        }
        return hours * 3600 + minutes * 60 + seconds;
    }

    /// <summary>Validates absolute volume without clamping a user's request.</summary>
    /// <param name="value">Requested value.</param>
    /// <returns>Protocol decimal.</returns>
    private static string VolumeValue(int? value) => value is >= 0 and <= 100 ?
        value.Value.ToString(CultureInfo.InvariantCulture) : throw new MediaDeviceException("invalid_volume");

    /// <summary>Requires a one-based bounded queue position.</summary>
    /// <param name="value">Requested index.</param>
    /// <returns>Validated index.</returns>
    private static int PositiveIndex(int? value) => value is >= 1 and <= 100_000 ? value.Value :
        throw new MediaDeviceException("invalid_queue_index");

    /// <summary>Rejects read actions from the dispatch surface.</summary>
    /// <param name="service">Service name.</param>
    /// <param name="action">Action name.</param>
    /// <returns>Whether the exact action writes device state.</returns>
    private static bool IsWriteAction(string service, string action) => service switch
    {
        "AVTransport" => action is "Play" or "Pause" or "Stop" or "Next" or "Previous" or "Seek" or "SetPlayMode" or
            "SetCrossfadeMode" or "SetAVTransportURI" or "AddURIToQueue" or "RemoveTrackRangeFromQueue" or
            "ReorderTracksInQueue" or "RemoveAllTracksFromQueue" or "BecomeCoordinatorOfStandaloneGroup",
        "RenderingControl" => action is "SetVolume" or "SetMute",
        "GroupRenderingControl" => action is "SnapshotGroupVolume" or "SetGroupVolume" or "SetGroupMute",
        _ => false
    };

    /// <summary>Recognizes protocol and transport failures that must not leak device payloads.</summary>
    /// <param name="exception">Observed failure.</param>
    /// <returns>Whether a stable public device code should replace it.</returns>
    private static bool IsDeviceFailure(Exception exception) => exception is FormatException or InvalidDataException or
        IOException or HttpRequestException or InvalidOperationException or MediaSoapFaultException or ArgumentException;

    /// <summary>Private expiring reference to browsed object and its original metadata.</summary>
    /// <param name="DeviceKey">Browsing player.</param>
    /// <param name="Household">Validated household.</param>
    /// <param name="Kind">Browse source.</param>
    /// <param name="Entry">Private DIDL object.</param>
    /// <param name="Revision">Browse revision.</param>
    /// <param name="Index">One-based queue index captured by the browse page.</param>
    /// <param name="ExpiresUtc">Expiry.</param>
    private sealed record MediaReference(string DeviceKey, string Household, string Kind, MediaBrowseEntry Entry,
        string Revision, int? Index, DateTimeOffset ExpiresUtc);

    /// <summary>Short-lived device-reported value read only after an explicitly dispatched snapshot step.</summary>
    /// <param name="Volume">Reported group volume.</param>
    /// <param name="ExpiresUtc">Maximum display age.</param>
    private sealed record GroupVolumeObservation(int Volume, DateTimeOffset ExpiresUtc);
}
