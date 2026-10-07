using MongoDB.Bson;
using MongoDB.Driver;
using SonosMonitoring.Application.MediaControl;
using SonosMonitoring.Platform;

namespace SonosMonitoring.Infrastructure.MediaControl;

/// <summary>Checks the standard merged document watermark using the existing projection database.</summary>
public sealed class MongoMediaOperationProjection : IMediaOperationProjection
{
    private readonly IMongoCollection<BsonDocument> _collection;

    /// <summary>Uses the installation's shared MongoDB client.</summary>
    /// <param name="database">Configured projection database.</param>
    public MongoMediaOperationProjection(IMongoDatabase database) =>
        this._collection = database.GetCollection<BsonDocument>("Merged_" + MediaControlNames.AGGREGATE);

    /// <inheritdoc />
    public async Task<long?> SequenceAsync(Guid id, CancellationToken ct)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", SonosMonitoring.Domain.MediaControl.MediaOperationId.With(id).ToString()) &
            Builders<BsonDocument>.Filter.Eq("tenantId", MonitoringNames.TENANT);
        var document = await this._collection.Find(filter).FirstOrDefaultAsync(ct);
        return document is null ? null : document["eventSequence"].ToInt64();
    }
}
