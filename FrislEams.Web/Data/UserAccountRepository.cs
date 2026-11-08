using System.Text.RegularExpressions;
using FrislEams.Web.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FrislEams.Web.Data;

public static class UserAccountRepository
{
    public static FilterDefinition<UserAccount> CaseInsensitiveUsernameFilter(string username)
        => Builders<UserAccount>.Filter.Regex(
            u => u.Username,
            new BsonRegularExpression($"^{Regex.Escape(username.Trim())}$", "i"));

    public static async Task<UserAccount?> FindByUsernameAsync(
        IMongoCollection<UserAccount> collection,
        string username,
        CancellationToken cancellationToken = default)
        => await collection
            .Find(CaseInsensitiveUsernameFilter(username))
            .FirstOrDefaultAsync(cancellationToken);

    public static async Task EnsureRequiredUsersAsync(
        AppDbContext db,
        CancellationToken cancellationToken = default)
    {
        var collection = db.UserAccounts.Collection;

        // Only create the built-in accounts on a brand-new database. Once any account exists,
        // admins own them: passwords, deactivations and deletions must survive a restart.
        if (await collection.CountDocumentsAsync(FilterDefinition<UserAccount>.Empty, cancellationToken: cancellationToken) > 0)
        {
            return;
        }

        foreach (var (username, password, role, displayName) in RequiredUsers)
        {
            await EnsureUserAsync(db, collection, username, password, role, displayName, cancellationToken);
        }

        var legacy = await FindByUsernameAsync(collection, "Backoffice", cancellationToken);
        if (legacy is not null)
        {
            await collection.DeleteOneAsync(
                Builders<UserAccount>.Filter.Eq(u => u.Id, legacy.Id),
                cancellationToken);
            Console.WriteLine("FRISL EAMS startup: removed legacy user 'Backoffice'.");
        }
    }

    private static readonly (string Username, string Password, string Role, string DisplayName)[] RequiredUsers =
    [
        ("Admin", "Washington", "Admin", "Admin"),
        ("Washington", "Washington1", "Backoffice", "Washington"),
        ("Staff", "Staff1", "Staff", "Staff"),
        ("Auditor", "auditor1", "Auditor", "Auditor"),
        ("emmanuel", "Washington", "Staff", "Emmanuel"),
        ("abayomi", "Washington", "Staff", "Abayomi"),
    ];

    private static async Task EnsureUserAsync(
        AppDbContext db,
        IMongoCollection<UserAccount> collection,
        string username,
        string password,
        string role,
        string displayName,
        CancellationToken cancellationToken)
    {
        var filter = CaseInsensitiveUsernameFilter(username);
        var matches = await collection.Find(filter).ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            var id = await db.NextIdAsync("users", cancellationToken);
            var user = new UserAccount
            {
                Id = id,
                Username = username,
                Password = password,
                Role = role,
                DisplayName = displayName,
                IsActive = true
            };
            await collection.InsertOneAsync(user, cancellationToken: cancellationToken);
            Console.WriteLine($"FRISL EAMS startup: created required user '{username}'.");
            return;
        }

        var primary = matches[0];
        foreach (var duplicate in matches.Skip(1))
        {
            await collection.DeleteOneAsync(
                Builders<UserAccount>.Filter.Eq(u => u.Id, duplicate.Id),
                cancellationToken);
            Console.WriteLine(
                $"FRISL EAMS startup: removed duplicate user '{duplicate.Username}' (keeping id {primary.Id}).");
        }

        var update = Builders<UserAccount>.Update
            .Set(u => u.Username, username)
            .Set(u => u.Password, password)
            .Set(u => u.Role, role)
            .Set(u => u.DisplayName, displayName)
            .Set(u => u.IsActive, true)
            .Set(u => u.UpdatedAt, DateTime.UtcNow);

        await collection.UpdateOneAsync(
            Builders<UserAccount>.Filter.Eq(u => u.Id, primary.Id),
            update,
            cancellationToken: cancellationToken);
        Console.WriteLine($"FRISL EAMS startup: ensured required user '{username}' (id {primary.Id}).");
    }
}
