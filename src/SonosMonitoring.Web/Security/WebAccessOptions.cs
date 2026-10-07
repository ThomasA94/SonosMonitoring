using System.Security.Cryptography;
using System.Text;

namespace SonosMonitoring.Web.Security;

/// <summary>Validates the operator-configured UI secret independently of the backend credential.</summary>
public sealed class WebAccessOptions
{
    private readonly byte[]? _keyHash;

    /// <summary>Rejects short configured secrets before accepting traffic.</summary>
    /// <param name="accessKey">Optional shared installation secret; absence selects loopback-only access.</param>
    public WebAccessOptions(string? accessKey)
    {
        if (accessKey is not null && accessKey.Length < 32)
        {
            throw new InvalidOperationException("Web:AccessKey must contain at least 32 characters.");
        }
        this._keyHash = accessKey is null ? null : SHA256.HashData(Encoding.UTF8.GetBytes(accessKey));
    }

    /// <summary>Gets whether cookie authentication is required, including for loopback clients.</summary>
    public bool RequiresLogin => this._keyHash is not null;

    /// <summary>Compares a submitted credential without exposing the configured secret.</summary>
    /// <param name="candidate">User-supplied shared access key.</param>
    public bool Matches(string candidate) => this._keyHash is not null && candidate.Length <= 1024 &&
        CryptographicOperations.FixedTimeEquals(this._keyHash, SHA256.HashData(Encoding.UTF8.GetBytes(candidate)));
}
