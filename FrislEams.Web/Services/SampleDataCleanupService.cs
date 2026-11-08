using FrislEams.Web.Data;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FrislEams.Web.Services;

/// <summary>
/// Removes the demo records that Data/SeedData.cs creates on a new database (sample assets and
/// everything hanging off them, plus sample lookups, suppliers and staff). Records are identified by
/// the exact codes and texts the seed uses. Lookups (departments, locations, categories, staff, ...)
/// are only removed when nothing else in the database still points at them, so real data that was
/// linked to a sample lookup is never orphaned. Login accounts are never touched.
/// </summary>
public sealed class SampleDataCleanupService(AppDbContext db)
{
    private static readonly string[] SampleAssetTags =
    [
        "FRISL-2022-CMP-IT-001", "FRISL-2022-CMP-FIN-001", "FRISL-2023-FUR-HR-001", "FRISL-2021-NET-IT-001",
        "FRISL-2023-PRT-OPS-001", "FRISL-2022-ACU-FIN-001", "FRISL-2023-CMP-OPS-001", "FRISL-2021-SRV-IT-001",
        "FRISL-2023-SEC-HQ-001", "FRISL-2020-FUR-LEG-001", "FRISL-2024-CMP-AUD-001", "FRISL-2022-VEH-OPS-001",
        "FRISL-2023-CMP-INV-001", "FRISL-2019-FUR-OPS-001", "FRISL-2024-NET-IT-001", "FRISL-2020-OEQ-HR-001"
    ];

    private static readonly string[] SampleRfidEventCodes = ["RFID-100112", "RFID-UNKNOWN-X1", "RFID-100101"];

    private static readonly string[] SampleNotificationTitles =
    [
        "Warranty Expiry Alert", "Pending Repair Review", "External Loan Active", "Asset Request Pending", "Audit Scheduled"
    ];

    private static readonly string[] SampleNotificationMessages =
    [
        "Cisco Catalyst 2960 Switch warranty expired 05-Sep-2024. Review recommended.",
        "HP LaserJet Pro M428fdn has an open repair request awaiting approval.",
        "Toyota Hilux GR Sport is on external loan. Exit grant expires in 5 days.",
        "2 asset requests are pending admin approval.",
        "IT department annual audit is in progress. Please complete variance analysis."
    ];

    private static readonly string[] SampleAssetRequestTexts =
    [
        "Need a new laptop for the new HR officer joining next month.",
        "Canon printer in HR is damaged beyond economic repair. Requesting replacement."
    ];

    // FR-009 and FR-010 (Emmanuel, Abayomi) are linked to built-in logins, so they stay.
    // Ngozi Abara (login "ithod", staff FR-011) is a made-up demo person and is removed outright.
    private static readonly string[] SampleStaffIds = ["FR-001", "FR-002", "FR-003", "FR-004", "FR-005", "FR-006", "FR-007", "FR-008"];
    private static readonly string[] SampleDepartmentCodes = ["IT", "FIN", "HR", "OPS", "LEG", "AUD", "INV", "CRP"];
    private static readonly string[] SampleLocationCodes = ["HQ-ABJ", "LG-BRN", "IB-BRN", "PH-BRN", "HQ-BLD", "HQ-GF", "SRV-HQ", "STORE", "ARCH"];
    private static readonly string[] SampleCategoryCodes = ["CMP", "FUR", "OEQ", "VEH", "NET", "SEC", "PRT", "SRV", "COM", "ACU"];
    private static readonly string[] SampleAssetTypeCodes = ["LAP", "DSK", "CHR", "SWT", "LPR"];
    private static readonly string[] SampleManufacturerCodes = ["DELL", "HP", "LEN", "CSCO", "APPL"];
    private static readonly string[] SampleSupplierCodes = ["TECHM", "FURNI", "NETPRO", "SECPT"];
    private static readonly string[] SampleContractorCodes = ["QFIX", "PCOOL", "ALFIX"];

    private static readonly HashSet<string> SystemCollections = new(StringComparer.OrdinalIgnoreCase)
    {
        "counters", "sessions", "dataProtectionKeys", "famas_import_map"
    };

    /// <summary>Counts (preview) or deletes (apply) the sample records. Returns label → count.</summary>
    public async Task<List<(string Label, long Count)>> RunAsync(bool apply, CancellationToken ct = default)
    {
        var results = new List<(string Label, long Count)>();
        var database = db.Database;

        // 1. Sample assets, excluding anything the FAMAS import owns.
        var famasAssetIds = (await database.GetCollection<BsonDocument>("famas_import_map")
                .Find(Builders<BsonDocument>.Filter.Regex("_id", new BsonRegularExpression("^asset:")))
                .ToListAsync(ct))
            .Select(d => d["AppId"].ToInt32())
            .ToHashSet();
        var assets = database.GetCollection<BsonDocument>("assets");
        var sampleAssetIds = (await assets.Find(Builders<BsonDocument>.Filter.In("TagCode", SampleAssetTags)).ToListAsync(ct))
            .Select(d => d["_id"].ToInt32())
            .Where(id => !famasAssetIds.Contains(id))
            .Select(id => (BsonValue)id)
            .ToList();

        var byAsset = Builders<BsonDocument>.Filter.In("AssetId", sampleAssetIds);
        results.Add(await RemoveAsync("RFID tags on sample assets", "rfidTags", byAsset, apply, ct));
        results.Add(await RemoveAsync("Status history", "assetStatusHistories", byAsset, apply, ct));
        results.Add(await RemoveAsync("Assignments", "assetAssignments", byAsset, apply, ct));
        results.Add(await RemoveAsync("Repair requests", "repairRequests", byAsset, apply, ct));
        results.Add(await RemoveAsync("Exit grants", "exitGrants", byAsset, apply, ct));
        results.Add(await RemoveAsync("Loan requests", "loanRequests", byAsset, apply, ct));
        results.Add(await RemoveAsync("Transfers", "assetTransfers", byAsset, apply, ct));
        results.Add(await RemoveAsync("Sample assets", "assets", Builders<BsonDocument>.Filter.In("_id", sampleAssetIds), apply, ct));

        // 2. Other sample activity, matched on the exact seed values.
        results.Add(await RemoveAsync("RFID gate events", "rfidEvents", Builders<BsonDocument>.Filter.In("RfidCode", SampleRfidEventCodes), apply, ct));
        results.Add(await RemoveAsync("Asset requests", "assetRequests", Builders<BsonDocument>.Filter.In("Description", SampleAssetRequestTexts), apply, ct));
        results.Add(await RemoveAsync("Audit sessions", "auditSessions",
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("Notes", "Annual IT asset verification"),
                Builders<BsonDocument>.Filter.Eq("InitiatedBy", "Damilola Afolabi")), apply, ct));
        results.Add(await RemoveAsync("Notifications", "notifications",
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.In("Title", SampleNotificationTitles),
                Builders<BsonDocument>.Filter.In("Message", SampleNotificationMessages)), apply, ct));

        // 3. The fictional "Ngozi Abara" demo account: login and staff record.
        results.Add(await RemoveAsync("Demo login: Ngozi Abara (ithod)", db.UserAccounts.Collection.CollectionNamespace.CollectionName,
            Builders<BsonDocument>.Filter.Regex("Username", new BsonRegularExpression("^ithod$", "i")), apply, ct));
        results.Add(await RemoveAsync("Demo staff: Ngozi Abara", "staff",
            Builders<BsonDocument>.Filter.Eq("StaffId", "FR-011"), apply, ct));

        // 4. Sample lookups, only where nothing still uses them. Order matters: staff and asset
        //    types first, because they point at departments and categories.
        results.Add(await RemoveUnreferencedAsync("Sample staff", "staff", "StaffId", SampleStaffIds,
            ["CurrentCustodianId", "AssignedToStaffId", "ConfirmedByStaffId", "RejectedByStaffId", "RequestedByStaffId", "ReportedByStaffId"], apply, ct));
        results.Add(await RemoveUnreferencedAsync("Sample asset types", "assetTypes", "Code", SampleAssetTypeCodes, ["AssetTypeId"], apply, ct));
        results.Add(await RemoveUnreferencedAsync("Sample manufacturers", "manufacturers", "Code", SampleManufacturerCodes, ["ManufacturerId"], apply, ct));
        results.Add(await RemoveUnreferencedAsync("Sample suppliers", "suppliers", "Code", SampleSupplierCodes, ["SupplierId"], apply, ct));
        results.Add(await RemoveUnreferencedAsync("Sample repair contractors", "repairContractors", "Code", SampleContractorCodes, ["AssignedContractorId", "ContractorId"], apply, ct));
        results.Add(await RemoveUnreferencedAsync("Sample departments", "departments", "Code", SampleDepartmentCodes,
            ["DepartmentId", "CurrentDepartmentId", "FromDepartmentId", "ToDepartmentId", "AssignedToDepartmentId"], apply, ct));
        results.Add(await RemoveUnreferencedAsync("Sample locations", "locations", "Code", SampleLocationCodes,
            ["LocationId", "CurrentLocationId", "FromLocationId", "ToLocationId", "AssignedLocationId", "BranchLocationId", "ParentLocationId"], apply, ct));
        results.Add(await RemoveUnreferencedAsync("Sample asset categories", "assetCategories", "Code", SampleCategoryCodes, ["AssetCategoryId"], apply, ct));

        return results;
    }

    private async Task<(string, long)> RemoveAsync(string label, string collectionName, FilterDefinition<BsonDocument> filter, bool apply, CancellationToken ct)
    {
        var collection = db.Database.GetCollection<BsonDocument>(collectionName);
        if (!apply) return (label, await collection.CountDocumentsAsync(filter, cancellationToken: ct));
        var deleted = await collection.DeleteManyAsync(filter, ct);
        return (label, deleted.DeletedCount);
    }

    /// <summary>Removes seed lookup records (matched on <paramref name="codeField"/>) that no other document references.</summary>
    private async Task<(string, long)> RemoveUnreferencedAsync(string label, string collectionName, string codeField, string[] codes,
        string[] referenceFields, bool apply, CancellationToken ct)
    {
        var collection = db.Database.GetCollection<BsonDocument>(collectionName);
        var candidates = await collection.Find(Builders<BsonDocument>.Filter.In(codeField, codes)).ToListAsync(ct);
        if (candidates.Count == 0) return (label, 0);

        // Preview: sample assets still exist and still point at these, so report the most that can go.
        if (!apply) return (label, candidates.Count);

        var otherCollections = (await (await db.Database.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct))
            .Where(n => !SystemCollections.Contains(n) && !n.StartsWith("famas_", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("system.", StringComparison.Ordinal))
            .ToList();

        long removed = 0;
        foreach (var candidate in candidates)
        {
            var id = candidate["_id"];
            var inUse = false;
            var refFilter = Builders<BsonDocument>.Filter.Or(referenceFields.Select(f => Builders<BsonDocument>.Filter.Eq(f, id)));
            foreach (var name in otherCollections)
            {
                var other = db.Database.GetCollection<BsonDocument>(name);
                var filter = name == collectionName
                    ? Builders<BsonDocument>.Filter.And(refFilter, Builders<BsonDocument>.Filter.Ne("_id", id))
                    : refFilter;
                if (await other.Find(filter).Limit(1).AnyAsync(ct))
                {
                    inUse = true;
                    break;
                }
            }

            if (inUse) continue;
            if (apply) await collection.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", id), ct);
            removed++;
        }

        return (label, removed);
    }
}
