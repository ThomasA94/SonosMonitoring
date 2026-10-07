using System.Text.Json;
using Devbone.Frameworks.Ddd.Repositories;
using SonosMonitoring.Domain.MediaControl;

namespace SonosMonitoring.Application.MediaControl;

/// <summary>Reads the latest durable operation state and matching concurrency token.</summary>
public sealed class MediaOperationReader
{
    private readonly IEventStreamReader _reader;

    /// <summary>Receives the framework event reader.</summary>
    /// <param name="reader">Reader for retained operation events.</param>
    public MediaOperationReader(IEventStreamReader reader) => this._reader = reader;

    /// <summary>Reads the latest complete event state; an absent stream returns null.</summary>
    /// <param name="operationId">Stable operation identity.</param>
    /// <param name="cancellationToken">Cancellation of the caller's read.</param>
    public Task<MediaOperationView?> ReadAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MediaOperationId id = MediaOperationId.With(operationId);
        var stored = this._reader.LoadAggregate(id).LastOrDefault();
        if (stored is null)
        {
            return Task.FromResult<MediaOperationView?>(null);
        }
        using JsonDocument payload = JsonDocument.Parse(stored.Value);
        JsonElement root = payload.RootElement;
        MediaOperationState state = new(id,
            this.ReadRequired(root, "action").GetString() ?? string.Empty,
            this.ReadRequired(root, "deviceKey").GetString() ?? string.Empty,
            this.ReadRequired(root, "payloadJson").GetString() ?? string.Empty,
            this.ReadRequired(root, "fingerprint").GetString() ?? string.Empty,
            this.ReadRequired(root, "generation").GetString() ?? string.Empty,
            this.ReadRequired(root, "acceptedAtUtc").GetDateTimeOffset(),
            this.ReadRequired(root, "expiresAtUtc").GetDateTimeOffset(),
            this.ReadRequired(root, "status").GetString() ?? string.Empty,
            this.TryRead(root, "step", out JsonElement step) ? step.GetInt32() : -1,
            this.ReadOptionalString(root, "outcome"),
            this.ReadOptionalString(root, "resultCode"));
        return Task.FromResult<MediaOperationView?>(new MediaOperationView(state,
            stored.AggregateConcurrencyToken?.ToString() ?? string.Empty, stored.EventSequence));
    }

    /// <summary>Reads a required persisted field across the framework's serializer naming policies.</summary>
    /// <param name="root">Persisted event object.</param>
    /// <param name="name">Logical camel-case field name.</param>
    private JsonElement ReadRequired(JsonElement root, string name) =>
        this.TryRead(root, name, out JsonElement value) ? value : throw new JsonException($"Missing media event field {name}.");

    /// <summary>Finds camel-case or Pascal-case event fields without changing event semantics.</summary>
    /// <param name="root">Persisted event object.</param>
    /// <param name="name">Logical camel-case field name.</param>
    /// <param name="value">Located JSON value when successful.</param>
    private bool TryRead(JsonElement root, string name, out JsonElement value) =>
        root.TryGetProperty(name, out value) ||
        root.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out value);

    /// <summary>Reads a field that older or default-omitting event payloads may leave absent.</summary>
    /// <param name="root">Persisted event object.</param>
    /// <param name="name">Expected camel-case field name.</param>
    private string ReadOptionalString(JsonElement root, string name) =>
        this.TryRead(root, name, out JsonElement value) ? value.GetString() ?? string.Empty : string.Empty;
}
