using System.Security.Cryptography;
using System.Text;
using FrislEams.Web.Data;
using FrislEams.Web.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FrislEams.Web.Services;

/// <summary>
/// Bearer tokens for the mobile app. A token is a random 32-byte value given to the app once at
/// sign-in; only its SHA-256 hash is stored (collection "apiTokens"), with a TTL index for expiry.
/// </summary>
public sealed class ApiTokenService(AppDbContext db)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    public const string ItemKey = "ApiUser";

    private IMongoCollection<BsonDocument> Tokens => db.Database.GetCollection<BsonDocument>("apiTokens");

    /// <summary>Checks a username and password the same way the web sign-in does.</summary>
    public async Task<UserAccount?> AuthenticateAsync(string? username, string? password, CancellationToken ct = default)
    {
        var name = username?.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(password)) return null;

        var user = await UserAccountRepository.FindByUsernameAsync(db.UserAccounts.Collection, name.ToLowerInvariant(), ct);
        if (user is null || !user.IsActive || user.Password != password) return null;
        return user;
    }

    public async Task<(string Token, DateTime ExpiresAt)> IssueAsync(UserAccount user, string? device, CancellationToken ct = default)
    {
        await EnsureIndexAsync(ct);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expiresAt = DateTime.UtcNow.Add(Lifetime);
        await Tokens.InsertOneAsync(new BsonDocument
        {
            { "_id", Hash(token) },
            { "UserId", user.Id },
            { "Username", user.Username },
            { "Device", string.IsNullOrWhiteSpace(device) ? BsonNull.Value : (BsonValue)device.Trim() },
            { "CreatedAt", DateTime.UtcNow },
            { "LastUsedAt", DateTime.UtcNow },
            { "ExpiresAt", expiresAt }
        }, cancellationToken: ct);
        return (token, expiresAt);
    }

    /// <summary>Returns the active user for a token, or null if the token is unknown, expired or the user is disabled.</summary>
    public async Task<UserAccount?> ValidateAsync(string token, CancellationToken ct = default)
    {
        var doc = await Tokens.Find(Builders<BsonDocument>.Filter.Eq("_id", Hash(token))).FirstOrDefaultAsync(ct);
        if (doc is null || doc["ExpiresAt"].ToUniversalTime() <= DateTime.UtcNow) return null;

        var user = await db.UserAccounts.Collection.Find(u => u.Id == doc["UserId"].ToInt32()).FirstOrDefaultAsync(ct);
        if (user is null || !user.IsActive) return null;

        // Record use at most every few minutes to keep writes down.
        if (DateTime.UtcNow - doc["LastUsedAt"].ToUniversalTime() > TimeSpan.FromMinutes(5))
        {
            await Tokens.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]),
                Builders<BsonDocument>.Update.Set("LastUsedAt", DateTime.UtcNow), cancellationToken: ct);
        }

        return user;
    }

    public Task RevokeAsync(string token, CancellationToken ct = default)
        => Tokens.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", Hash(token)), ct);

    public static string? ReadBearer(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task EnsureIndexAsync(CancellationToken ct)
    {
        try
        {
            await Tokens.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("ExpiresAt"),
                new CreateIndexOptions { Name = "ExpiresAt_ttl", ExpireAfter = TimeSpan.Zero }), cancellationToken: ct);
        }
        catch (MongoException)
        {
            // Already exists. Expired tokens are also rejected on read.
        }
    }
}
