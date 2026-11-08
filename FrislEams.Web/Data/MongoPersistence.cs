using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Caching.Distributed;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FrislEams.Web.Data;

/// <summary>
/// Stores ASP.NET Core data-protection keys in MongoDB. These keys encrypt the session and
/// antiforgery cookies. Kept in memory they changed on every restart or deploy, which made
/// every existing cookie unreadable and signed everyone out.
/// </summary>
public sealed class MongoXmlRepository(IMongoDatabase database) : IXmlRepository
{
    private readonly IMongoCollection<BsonDocument> keys = database.GetCollection<BsonDocument>("dataProtectionKeys");

    public IReadOnlyCollection<XElement> GetAllElements()
        => keys.Find(FilterDefinition<BsonDocument>.Empty)
            .ToList()
            .Select(d => XElement.Parse(d["Xml"].AsString))
            .ToList();

    public void StoreElement(XElement element, string friendlyName)
    {
        var id = string.IsNullOrWhiteSpace(friendlyName) ? Guid.NewGuid().ToString("N") : friendlyName;
        keys.ReplaceOne(
            Builders<BsonDocument>.Filter.Eq("_id", id),
            new BsonDocument
            {
                { "_id", id },
                { "Xml", element.ToString(SaveOptions.DisableFormatting) },
                { "StoredAt", DateTime.UtcNow }
            },
            new ReplaceOptions { IsUpsert = true });
    }
}

/// <summary>
/// IDistributedCache backed by MongoDB, used for session storage so sign-ins survive restarts
/// and work across more than one app instance. Expired entries are removed by a TTL index.
/// </summary>
public sealed class MongoDistributedCache : IDistributedCache
{
    private readonly IMongoCollection<BsonDocument> entries;

    public MongoDistributedCache(IMongoDatabase database)
    {
        entries = database.GetCollection<BsonDocument>("sessions");
        try
        {
            entries.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("ExpiresAt"),
                new CreateIndexOptions { Name = "ExpiresAt_ttl", ExpireAfter = TimeSpan.Zero }));
        }
        catch (MongoException)
        {
            // Index already exists with other options, or the database is briefly unavailable.
            // Expired entries are still ignored on read.
        }
    }

    public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult();

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        var doc = await entries.Find(ById(key)).FirstOrDefaultAsync(token);
        if (doc is null) return null;

        var now = DateTime.UtcNow;
        if (doc["ExpiresAt"].ToUniversalTime() <= now)
        {
            await entries.DeleteOneAsync(ById(key), token);
            return null;
        }

        await SlideAsync(doc, now, token);
        return doc["Value"].AsByteArray;
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        => SetAsync(key, value, options).GetAwaiter().GetResult();

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        var now = DateTime.UtcNow;
        DateTime? absolute = null;
        if (options.AbsoluteExpiration.HasValue) absolute = options.AbsoluteExpiration.Value.UtcDateTime;
        else if (options.AbsoluteExpirationRelativeToNow.HasValue) absolute = now + options.AbsoluteExpirationRelativeToNow.Value;
        var sliding = options.SlidingExpiration;

        var expiresAt = sliding is { } s ? now + s : absolute ?? now.AddHours(8);
        if (absolute is { } a && a < expiresAt) expiresAt = a;

        var doc = new BsonDocument
        {
            { "_id", key },
            { "Value", new BsonBinaryData(value) },
            { "ExpiresAt", expiresAt },
            { "SlidingSeconds", sliding.HasValue ? (BsonValue)sliding.Value.TotalSeconds : BsonNull.Value },
            { "AbsoluteExpiration", absolute.HasValue ? (BsonValue)absolute.Value : BsonNull.Value }
        };
        await entries.ReplaceOneAsync(ById(key), doc, new ReplaceOptions { IsUpsert = true }, token);
    }

    public void Refresh(string key) => RefreshAsync(key).GetAwaiter().GetResult();

    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        var doc = await entries.Find(ById(key)).FirstOrDefaultAsync(token);
        if (doc is not null) await SlideAsync(doc, DateTime.UtcNow, token);
    }

    public void Remove(string key) => entries.DeleteOne(ById(key));

    public Task RemoveAsync(string key, CancellationToken token = default) => entries.DeleteOneAsync(ById(key), token);

    private async Task SlideAsync(BsonDocument doc, DateTime now, CancellationToken token)
    {
        if (!doc.TryGetValue("SlidingSeconds", out var s) || s.IsBsonNull) return;

        var expiresAt = now.AddSeconds(s.ToDouble());
        if (doc.TryGetValue("AbsoluteExpiration", out var abs) && !abs.IsBsonNull && abs.ToUniversalTime() < expiresAt)
        {
            expiresAt = abs.ToUniversalTime();
        }

        await entries.UpdateOneAsync(ById(doc["_id"].AsString), Builders<BsonDocument>.Update.Set("ExpiresAt", expiresAt), cancellationToken: token);
    }

    private static FilterDefinition<BsonDocument> ById(string key) => Builders<BsonDocument>.Filter.Eq("_id", key);
}
