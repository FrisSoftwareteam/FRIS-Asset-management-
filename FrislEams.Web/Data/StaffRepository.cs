using FrislEams.Web.Models;
using MongoDB.Driver;

namespace FrislEams.Web.Data;

public static class StaffRepository
{
    /// <summary>
    /// Resolves the Staff record for the logged-in portal user (e.g. username "emmanuel" → Emmanuel FR-009).
    /// </summary>
    public static async Task<Staff?> FindBySessionAsync(
        AppDbContext db,
        string? username,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var staffList = await db.Staff.AsQueryable().ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(username))
        {
            var byEmailLocal = staffList.FirstOrDefault(s =>
            {
                var local = s.Email.Split('@')[0];
                return local.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase);
            });
            if (byEmailLocal is not null)
            {
                return byEmailLocal;
            }
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var byName = staffList.FirstOrDefault(s =>
                s.FullName.Equals(displayName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                return byName;
            }
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            return staffList.FirstOrDefault(s =>
                s.Email.StartsWith(username.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    public static async Task EnsureRequiredStaffAsync(
        AppDbContext db,
        CancellationToken cancellationToken = default)
    {
        var collection = db.Staff.Collection;
        var departments = await db.Departments.AsQueryable().ToListAsync(cancellationToken);
        var deptByCode = departments.ToDictionary(d => d.Code, d => d.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var (staffId, fullName, email, phone, role, departmentCode) in RequiredStaff)
        {
            if (!deptByCode.TryGetValue(departmentCode, out var departmentId))
            {
                Console.WriteLine(
                    $"FRISL EAMS startup: skipped required staff '{fullName}' — department '{departmentCode}' not found.");
                continue;
            }

            await EnsureStaffAsync(
                db,
                collection,
                staffId,
                fullName,
                email,
                phone,
                role,
                departmentId,
                cancellationToken);
        }
    }

    private static readonly (string StaffId, string FullName, string Email, string Phone, string Role, string DepartmentCode)[] RequiredStaff =
    [
        ("FR-009", "Emmanuel", "emmanuel@firstregistrars.ng", "0819-999-9999", "Staff", "IT"),
        ("FR-010", "Abayomi", "abayomi@firstregistrars.ng", "0820-000-0000", "Staff", "IT"),
    ];

    private static async Task EnsureStaffAsync(
        AppDbContext db,
        IMongoCollection<Staff> collection,
        string staffId,
        string fullName,
        string email,
        string phone,
        string role,
        int departmentId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<Staff>.Filter.Or(
            Builders<Staff>.Filter.Eq(s => s.StaffId, staffId),
            Builders<Staff>.Filter.Regex(s => s.Email, new MongoDB.Bson.BsonRegularExpression($"^{System.Text.RegularExpressions.Regex.Escape(email)}$", "i")),
            Builders<Staff>.Filter.Regex(s => s.FullName, new MongoDB.Bson.BsonRegularExpression($"^{System.Text.RegularExpressions.Regex.Escape(fullName)}$", "i")));

        var matches = await collection.Find(filter).ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            var id = await db.NextIdAsync("staff", cancellationToken);
            var staff = new Staff
            {
                Id = id,
                StaffId = staffId,
                FullName = fullName,
                Email = email,
                PhoneNumber = phone,
                Role = role,
                DepartmentId = departmentId,
                IsActive = true
            };
            await collection.InsertOneAsync(staff, cancellationToken: cancellationToken);
            Console.WriteLine($"FRISL EAMS startup: created required staff '{fullName}' ({staffId}).");
            return;
        }

        var primary = matches[0];
        foreach (var duplicate in matches.Skip(1))
        {
            await collection.DeleteOneAsync(
                Builders<Staff>.Filter.Eq(s => s.Id, duplicate.Id),
                cancellationToken);
            Console.WriteLine(
                $"FRISL EAMS startup: removed duplicate staff '{duplicate.FullName}' (keeping id {primary.Id}).");
        }

        var update = Builders<Staff>.Update
            .Set(s => s.StaffId, staffId)
            .Set(s => s.FullName, fullName)
            .Set(s => s.Email, email)
            .Set(s => s.PhoneNumber, phone)
            .Set(s => s.Role, role)
            .Set(s => s.DepartmentId, departmentId)
            .Set(s => s.IsActive, true);

        await collection.UpdateOneAsync(
            Builders<Staff>.Filter.Eq(s => s.Id, primary.Id),
            update,
            cancellationToken: cancellationToken);
        Console.WriteLine($"FRISL EAMS startup: ensured required staff '{fullName}' ({staffId}, id {primary.Id}).");
    }
}
