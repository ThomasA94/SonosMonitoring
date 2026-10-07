namespace SonosMonitoring.Application.MediaControl.Transport;

/// <summary>One immutable protocol step requiring an external durable one-attempt claim.</summary>
/// <param name="TargetDeviceKey">Stable target UDN.</param>
/// <param name="TargetAddress">Validated literal IPv4 address at preparation.</param>
/// <param name="Service">Whitelisted service name.</param>
/// <param name="Action">Whitelisted SOAP action.</param>
/// <param name="Arguments">Application-owned argument names and values.</param>
/// <param name="Context">Identity and topology binding.</param>
public sealed record MediaWriteStep(string TargetDeviceKey, string TargetAddress, string Service, string Action,
    IReadOnlyDictionary<string, string> Arguments, MediaTargetContext Context)
{
    /// <summary>Gets the source fingerprint required immediately before this step.</summary>
    public string? ExpectedSourceToken { get; init; }

    /// <summary>Gets a frozen destination group context for a join step.</summary>
    public MediaTargetContext? DestinationContext { get; init; }

    /// <summary>Gets the queue revision to recheck immediately before dispatch.</summary>
    public string? RequiredQueueRevision { get; init; }
}
