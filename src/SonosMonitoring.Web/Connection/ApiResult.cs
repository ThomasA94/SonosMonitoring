namespace SonosMonitoring.Web.Connection;

/// <summary>Separates valid data, absent policies, pending projections and transport failures.</summary>
/// <typeparam name="T">Expected transport payload.</typeparam>
/// <param name="Value">Decoded value when present.</param>
/// <param name="StatusCode">HTTP status or zero when no response arrived.</param>
/// <param name="ErrorCode">Safe machine-readable error category.</param>
public sealed record ApiResult<T>(T? Value, int StatusCode, string? ErrorCode)
{
    /// <summary>Gets whether a complete payload was received.</summary>
    public bool IsSuccess => this.ErrorCode is null && this.Value is not null;
}
