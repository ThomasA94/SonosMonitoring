using MongoDB.Bson;
using MongoDB.Driver;
using SonosMonitoring.Application.Monitoring;
using SonosMonitoring.Platform;

namespace SonosMonitoring.Infrastructure.Monitoring;

/// <summary>Maps the framework's standard merged document to the application's narrow query port.</summary>
public sealed class MongoMonitoringQuery : IMonitoringQuery
{
    private readonly IMongoCollection<BsonDocument> _collection;

    /// <summary>Uses the existing shared Mongo client and documented merged collection shape.</summary>
    /// <param name="database">The installation's projection database.</param>
    public MongoMonitoringQuery(IMongoDatabase database)
    {
        this._collection = database.GetCollection<BsonDocument>("Merged_" + MonitoringNames.AGGREGATE);
    }

    /// <inheritdoc />
    public async Task<MonitoringView?> FindAsync(string deviceKey, CancellationToken ct)
    {
        var id = MonitoringIdentity.ForDevice(deviceKey).ToString();
        var filter = Builders<BsonDocument>.Filter.Eq("_id", id) & Builders<BsonDocument>.Filter.Eq("tenantId", MonitoringNames.TENANT);
        var document = await this._collection.Find(filter).FirstOrDefaultAsync(ct);
        if (document is null)
        {
            return null;
        }
        var state = document["state"].AsBsonDocument;
        return new MonitoringView(id, state["DeviceKey"].AsString, state["Enabled"].AsBoolean, state["IntervalSeconds"].ToInt32(),
            document["eventSequence"].ToInt64(), new DateTimeOffset(document["updatedAtUtc"].ToUniversalTime()));
    }
}
