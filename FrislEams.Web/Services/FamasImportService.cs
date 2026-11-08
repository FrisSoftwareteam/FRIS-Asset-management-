using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FrislEams.Web.Data;
using FrislEams.Web.Domain;
using FrislEams.Web.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FrislEams.Web.Services;

/// <summary>Outcome of one FAMAS import run, shown on the Data import page.</summary>
public sealed class FamasImportResult
{
    public List<string> Messages { get; } = [];
    public List<string> Warnings { get; } = [];
    public Dictionary<string, int> RawTables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, (int Added, int Updated)> Mapped { get; } = new();
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
}

/// <summary>
/// Imports a JSON export of the legacy FAMAS SQL Server database.
/// 1. Every table is copied as-is into a "famas_&lt;table&gt;" collection (credentials removed).
/// 2. Core tables are mapped into the app's own collections (locations, departments, categories,
///    staff, assets, RFID tags, transfers and the activity log).
/// Safe to run more than once: records are matched on natural keys or on the legacy id
/// (kept in the "famas_import_map" collection), so a re-run updates instead of duplicating.
/// </summary>
public sealed partial class FamasImportService(AppDbContext db, ILogger<FamasImportService> logger)
{
    private const string RawPrefix = "famas_";
    private const string MapCollection = "famas_import_map";

    // Columns that are never copied: legacy passwords and password-reset answers.
    private static readonly HashSet<string> SensitiveColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "UPwd", "UPwd_old", "Upwd2", "R_Answer"
    };

    /// <summary>Reads uploaded files: either the combined _all_tables.json or individual dbo_*.json files.</summary>
    public static Dictionary<string, List<JsonElement>> ParseFiles(IEnumerable<(string FileName, string Content)> files, FamasImportResult result)
    {
        var tables = new Dictionary<string, List<JsonElement>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fileName, rawContent) in files)
        {
            var content = rawContent.TrimStart('﻿');
            // The FAMAS export writes one table name with a trailing backslash ("dbo.AssetAuditold\"),
            // which escapes the closing quote and breaks the JSON. Drop that stray backslash.
            content = StrayBackslashInTableName().Replace(content, "$1$2");

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(content, new JsonDocumentOptions { AllowTrailingCommas = true });
            }
            catch (JsonException ex)
            {
                result.Warnings.Add($"{fileName}: not valid JSON ({ex.Message}). Skipped.");
                continue;
            }

            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                result.Warnings.Add($"{fileName}: expected a JSON array. Skipped.");
                continue;
            }

            var first = root.EnumerateArray().FirstOrDefault();
            var isCombined = first.ValueKind == JsonValueKind.Object
                             && first.TryGetProperty("table", out _)
                             && first.TryGetProperty("rows", out _);

            if (isCombined)
            {
                foreach (var entry in root.EnumerateArray())
                {
                    var name = NormaliseTableName(entry.GetProperty("table").GetString() ?? "");
                    if (name.Length == 0) continue;
                    var rows = entry.GetProperty("rows");
                    tables[name] = rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray().ToList() : [];
                }
            }
            else
            {
                var name = NormaliseTableName(Path.GetFileNameWithoutExtension(fileName));
                if (name.Length == 0 || name.StartsWith("_", StringComparison.Ordinal)) continue;
                tables[name] = root.EnumerateArray().ToList();
            }
        }

        return tables;
    }

    public async Task<FamasImportResult> ImportAsync(Dictionary<string, List<JsonElement>> tables, string operatorName, FamasImportResult result, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        try
        {
            if (tables.Count == 0)
            {
                result.Warnings.Add("No tables were found in the uploaded files.");
                return result;
            }

            await CopyRawTablesAsync(tables, result, ct);

            var locations = await ImportLocationsAsync(Rows(tables, "locations"), result, ct);
            var departments = await ImportDepartmentsAsync(Rows(tables, "Departments"), result, ct);
            var categories = await ImportCategoriesAsync(Rows(tables, "assetcategory"), result, ct);
            var staffByName = await ImportStaffAsync(Rows(tables, "tblProfile"), departments, result, ct);
            var storeDepartments = Rows(tables, "Departments")
                .Where(r => Int(r, "StoreFlag") == 1)
                .Select(r => Int(r, "id") ?? 0)
                .ToHashSet();
            var assets = await ImportAssetsAsync(Rows(tables, "TagFile"), Rows(tables, "AllocateAssets"), Rows(tables, "Asset_Status"),
                locations, departments, categories, storeDepartments, staffByName, result, ct);
            await ImportRfidTagsAsync(Rows(tables, "TagFile"), assets, operatorName, result, ct);
            await ImportTransfersAsync(Rows(tables, "tblRelocateAssetHistory"), assets, departments, locations, result, ct);
            await ImportOperationLogAsync(Rows(tables, "OperationLog"), result, ct);

            await db.Database.GetCollection<BsonDocument>(RawPrefix + "import_runs").InsertOneAsync(new BsonDocument
            {
                { "RunAt", DateTime.UtcNow },
                { "Operator", operatorName },
                { "Tables", tables.Count },
                { "Rows", tables.Values.Sum(v => v.Count) }
            }, cancellationToken: ct);

            result.Success = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FAMAS import failed");
            result.Warnings.Add($"Import stopped with an error: {ex.Message}");
        }
        finally
        {
            result.Duration = DateTime.UtcNow - started;
        }

        return result;
    }

    // ---------------------------------------------------------------- raw copy

    private async Task CopyRawTablesAsync(Dictionary<string, List<JsonElement>> tables, FamasImportResult result, CancellationToken ct)
    {
        foreach (var (table, rows) in tables)
        {
            var collection = db.Database.GetCollection<BsonDocument>(RawPrefix + table);
            await collection.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty, ct);

            var docs = new List<BsonDocument>(rows.Count);
            foreach (var row in rows)
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                var doc = BsonDocument.Parse(row.GetRawText());
                foreach (var column in SensitiveColumns)
                {
                    doc.Remove(column);
                }

                // Keep the legacy "id" visible but let MongoDB assign its own _id.
                if (doc.Contains("_id")) doc.Remove("_id");
                docs.Add(doc);
            }

            if (docs.Count > 0)
            {
                foreach (var chunk in docs.Chunk(1000))
                {
                    await collection.InsertManyAsync(chunk, cancellationToken: ct);
                }
            }

            result.RawTables[RawPrefix + table] = docs.Count;
        }

        result.Messages.Add($"Copied {tables.Count} tables ({result.RawTables.Values.Sum():N0} rows) into famas_* collections. Passwords and security answers were left out.");
    }

    // ---------------------------------------------------------------- lookups

    private async Task<Dictionary<int, int>> ImportLocationsAsync(List<JsonElement> rows, FamasImportResult result, CancellationToken ct)
    {
        var collection = db.Locations.Collection;
        var existing = (await collection.Find(FilterDefinition<Location>.Empty).ToListAsync(ct))
            .GroupBy(l => Key(l.Name)).ToDictionary(g => g.Key, g => g.First());
        var map = new Dictionary<int, int>();
        int added = 0, updated = 0;

        var toInsert = rows.Count(r => !existing.ContainsKey(Key(Title(Str(r, "name")))));
        var ids = await PoolAsync("locations", toInsert, ct);

        foreach (var row in rows)
        {
            var legacyId = Int(row, "id") ?? 0;
            var name = Title(Str(row, "name"));
            if (name.Length == 0) continue;

            if (existing.TryGetValue(Key(name), out var found))
            {
                map[legacyId] = found.Id;
                updated++;
                continue;
            }

            var location = new Location
            {
                Id = await ids.NextAsync(),
                Name = name,
                Code = $"FAMAS-L{legacyId}",
                LocationType = name.Contains("HEAD OFFICE", StringComparison.OrdinalIgnoreCase) ? "Head Office" : "Branch"
            };
            await collection.InsertOneAsync(location, cancellationToken: ct);
            existing[Key(name)] = location;
            map[legacyId] = location.Id;
            added++;
        }

        result.Mapped["Locations"] = (added, updated);
        return map;
    }

    private async Task<Dictionary<int, int>> ImportDepartmentsAsync(List<JsonElement> rows, FamasImportResult result, CancellationToken ct)
    {
        var collection = db.Departments.Collection;
        var existing = (await collection.Find(FilterDefinition<Department>.Empty).ToListAsync(ct))
            .GroupBy(d => Key(d.Name)).ToDictionary(g => g.Key, g => g.First());
        var map = new Dictionary<int, int>();
        int added = 0, updated = 0;

        var toInsert = rows.Count(r => !existing.ContainsKey(Key(Title(Str(r, "name")))));
        var ids = await PoolAsync("departments", toInsert, ct);

        foreach (var row in rows)
        {
            var legacyId = Int(row, "id") ?? 0;
            var name = Title(Str(row, "name"));
            if (name.Length == 0) continue;

            if (existing.TryGetValue(Key(name), out var found))
            {
                map[legacyId] = found.Id;
                updated++;
                continue;
            }

            var department = new Department { Id = await ids.NextAsync(), Name = name, Code = $"FAMAS-{legacyId}" };
            await collection.InsertOneAsync(department, cancellationToken: ct);
            existing[Key(name)] = department;
            map[legacyId] = department.Id;
            added++;
        }

        result.Mapped["Departments"] = (added, updated);
        return map;
    }

    private async Task<Dictionary<int, int>> ImportCategoriesAsync(List<JsonElement> rows, FamasImportResult result, CancellationToken ct)
    {
        var collection = db.AssetCategories.Collection;
        var existing = (await collection.Find(FilterDefinition<AssetCategory>.Empty).ToListAsync(ct))
            .GroupBy(c => Key(c.Name)).ToDictionary(g => g.Key, g => g.First());
        var map = new Dictionary<int, int>();
        int added = 0, updated = 0;

        var toInsert = rows.Count(r => !existing.ContainsKey(Key(Title(Str(r, "name")))));
        var ids = await PoolAsync("assetCategories", toInsert, ct);

        foreach (var row in rows)
        {
            var legacyId = Int(row, "id") ?? 0;
            var name = Title(Str(row, "name"));
            if (name.Length == 0) continue;

            if (existing.TryGetValue(Key(name), out var found))
            {
                map[legacyId] = found.Id;
                updated++;
                continue;
            }

            var category = new AssetCategory
            {
                Id = await ids.NextAsync(),
                Name = name,
                Code = $"FAMAS-{legacyId:000}",
                UsefulLifeYears = 5
            };
            await collection.InsertOneAsync(category, cancellationToken: ct);
            existing[Key(name)] = category;
            map[legacyId] = category.Id;
            added++;
        }

        result.Mapped["Asset categories"] = (added, updated);
        return map;
    }

    /// <summary>FAMAS user profiles become Staff records (no login accounts, no passwords).</summary>
    private async Task<Dictionary<string, int>> ImportStaffAsync(List<JsonElement> rows, Dictionary<int, int> departments, FamasImportResult result, CancellationToken ct)
    {
        var collection = db.Staff.Collection;
        var existing = await collection.Find(FilterDefinition<Staff>.Empty).ToListAsync(ct);
        var byStaffId = existing.Where(s => !string.IsNullOrEmpty(s.StaffId))
            .GroupBy(s => s.StaffId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var byName = existing.GroupBy(s => Key(s.FullName)).ToDictionary(g => g.Key, g => g.First().Id);
        int added = 0, updated = 0;

        var fallbackDepartment = departments.Values.DefaultIfEmpty(0).First();
        var pending = rows.Where(r => Str(r, "UserName").Length > 0).ToList();
        var ids = await PoolAsync("staff", pending.Count(r => !byStaffId.ContainsKey($"FAMAS-{Str(r, "UserName")}")), ct);

        foreach (var row in pending)
        {
            var username = Str(row, "UserName");
            var staffId = $"FAMAS-{username}";
            var fullName = Title(Str(row, "UFullName"));
            if (fullName.Length == 0) fullName = username;
            var deptId = departments.GetValueOrDefault(Int(row, "deptid") ?? -1, fallbackDepartment);
            var role = Int(row, "iAdmin") == 1 ? "Admin"
                     : Int(row, "Unit_Head") == 1 ? "DepartmentHead"
                     : "Staff";
            var isActive = Int(row, "iEnable") == 1;

            if (byStaffId.TryGetValue(staffId, out var found))
            {
                var update = Builders<Staff>.Update
                    .Set(s => s.FullName, fullName)
                    .Set(s => s.DepartmentId, deptId)
                    .Set(s => s.Role, role)
                    .Set(s => s.IsActive, isActive);
                await collection.UpdateOneAsync(s => s.Id == found.Id, update, cancellationToken: ct);
                byName[Key(fullName)] = found.Id;
                updated++;
                continue;
            }

            var staff = new Staff
            {
                Id = await ids.NextAsync(),
                StaffId = staffId,
                FullName = fullName,
                Email = string.Empty,
                PhoneNumber = string.Empty,
                Role = role,
                DepartmentId = deptId,
                IsActive = isActive,
                JoinedAt = DateTime.UtcNow
            };
            await collection.InsertOneAsync(staff, cancellationToken: ct);
            byName[Key(fullName)] = staff.Id;
            added++;
        }

        result.Mapped["Staff"] = (added, updated);
        if (rows.Count > 0)
        {
            result.Messages.Add("FAMAS user profiles were added as staff records only. No login accounts were created and no passwords were copied.");
        }

        return byName;
    }

    // ---------------------------------------------------------------- assets

    private sealed record ImportedAsset(int AppId, int LegacyId, string TagCode);

    private async Task<Dictionary<int, ImportedAsset>> ImportAssetsAsync(
        List<JsonElement> tagFile, List<JsonElement> allocations, List<JsonElement> statusRows,
        Dictionary<int, int> locations, Dictionary<int, int> departments, Dictionary<int, int> categories,
        HashSet<int> storeDepartments, Dictionary<string, int> staffByName,
        FamasImportResult result, CancellationToken ct)
    {
        var collection = db.Assets.Collection;
        var existing = (await collection.Find(FilterDefinition<Asset>.Empty).ToListAsync(ct))
            .GroupBy(a => a.TagCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var legacyMap = await LoadMapAsync("asset", ct);
        var statusNames = statusRows.ToDictionary(r => Int(r, "id") ?? -1, r => Str(r, "name"));
        var categoryNames = new Dictionary<int, string>();
        foreach (var c in await db.AssetCategories.Collection.Find(FilterDefinition<AssetCategory>.Empty).ToListAsync(ct))
        {
            categoryNames[c.Id] = c.Name;
        }

        // Latest allocation per asset (prefer the active one) gives the receiving custodian.
        var latestAllocation = allocations
            .GroupBy(a => Int(a, "refid") ?? 0)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => Bool(a, "active")).ThenByDescending(a => Int(a, "id") ?? 0).First());

        var fallbackCategory = categories.Values.DefaultIfEmpty(0).First();
        var imported = new Dictionary<int, ImportedAsset>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int added = 0, updated = 0, duplicates = 0;

        var newCount = tagFile.Count(r => !legacyMap.ContainsKey(Int(r, "id") ?? 0));
        var ids = await PoolAsync("assets", newCount, ct);
        var writes = new List<WriteModel<Asset>>();
        var newMapEntries = new List<(string Key, int AppId)>();

        foreach (var row in tagFile)
        {
            var legacyId = Int(row, "id") ?? 0;
            if (legacyId == 0) continue;

            var tagCode = FirstNonEmpty(Str(row, "assigned_tagcode"), Str(row, "tagcode"), $"FAMAS-{legacyId}");
            if (!seenCodes.Add(tagCode))
            {
                // FAMAS has a few tag codes used by two records. Keep both, suffix the second.
                tagCode = $"{tagCode}-R{legacyId}";
                seenCodes.Add(tagCode);
                duplicates++;
            }

            var assignedDept = Int(row, "assigned_deptid") ?? 0;
            var legacyDept = assignedDept > 0 ? assignedDept : Int(row, "DeptId") ?? 0;
            var deptId = departments.TryGetValue(legacyDept, out var d) ? d : (int?)null;
            var locationId = locations.TryGetValue(Int(row, "locationID") ?? 0, out var l) ? l : (int?)null;
            var categoryId = categories.TryGetValue(Int(row, "CatId") ?? 0, out var c2) ? c2 : fallbackCategory;
            var statusId = Int(row, "status") ?? 0;
            var status = MapStatus(statusId, storeDepartments.Contains(legacyDept));
            var brand = Title(Str(row, "Brandname"));
            var categoryName = categoryNames.GetValueOrDefault(categoryId, "Asset");
            var gl = Str(row, "GL_Transaction_RefNo");
            if (gl.Equals("Not Specified", StringComparison.OrdinalIgnoreCase)) gl = string.Empty;

            int? custodianId = null;
            if (latestAllocation.TryGetValue(legacyId, out var alloc))
            {
                var receivedBy = Str(alloc, "received_by");
                if (receivedBy.Length > 0 && staffByName.TryGetValue(Key(receivedBy), out var sid)) custodianId = sid;
            }

            var appId = legacyMap.TryGetValue(legacyId, out var mapped) ? mapped
                      : existing.TryGetValue(tagCode, out var byCode) ? byCode.Id
                      : 0;
            var isNew = appId == 0;
            if (isNew) appId = await ids.NextAsync();

            var asset = isNew ? new Asset() : existing.Values.FirstOrDefault(a => a.Id == appId) ?? new Asset();
            asset.Id = appId;
            asset.TagCode = tagCode;
            asset.AssetName = brand.Length > 0 ? $"{categoryName} – {brand}" : categoryName;
            asset.Description = $"Imported from FAMAS (ref {legacyId}).";
            asset.AssetCategoryId = categoryId;
            asset.Brand = brand.Length > 0 ? brand : null;
            asset.SerialNumber = NullIfEmpty(Str(row, "serialnumber"));
            asset.GlCode = NullIfEmpty(gl);
            asset.PurchaseDate = Date(row, "Date_In");
            asset.CurrentStatus = status;
            asset.CurrentCondition = statusId is 2 or 3 or 11 ? "Damaged" : "Good";
            asset.CurrentDepartmentId = deptId;
            asset.CurrentLocationId = locationId;
            asset.CurrentCustodianId = custodianId;
            asset.Notes = $"FAMAS status: {statusNames.GetValueOrDefault(statusId, statusId.ToString(CultureInfo.InvariantCulture))}"
                          + (Bool(row, "active") ? "" : " (inactive in FAMAS)");
            if (isNew) asset.CreatedAt = Date(row, "generated_date") ?? Date(row, "Date_In") ?? DateTime.UtcNow;

            writes.Add(new ReplaceOneModel<Asset>(Builders<Asset>.Filter.Eq(a => a.Id, appId), asset) { IsUpsert = true });
            if (!legacyMap.ContainsKey(legacyId)) newMapEntries.Add(($"asset:{legacyId}", appId));
            imported[legacyId] = new ImportedAsset(appId, legacyId, tagCode);
            if (isNew) added++; else updated++;
        }

        foreach (var chunk in writes.Chunk(500))
        {
            await collection.BulkWriteAsync(chunk, new BulkWriteOptions { IsOrdered = false }, ct);
        }

        await SaveMapAsync(newMapEntries, ct);
        result.Mapped["Assets"] = (added, updated);
        if (duplicates > 0)
        {
            result.Warnings.Add($"{duplicates} FAMAS tag code(s) were used by two assets. The second one was imported with a “-R<ref>” suffix so both are kept.");
        }

        return imported;
    }

    private static AssetStatus MapStatus(int famasStatus, bool inStoreDepartment) => famasStatus switch
    {
        1 or 9 or 10 or 12 or 13 => inStoreDepartment ? AssetStatus.RegisteredUnassigned : AssetStatus.ActiveAssigned,
        8 => AssetStatus.RegisteredUnassigned,
        2 => AssetStatus.UnderRepair,
        3 or 11 => AssetStatus.Damaged,
        4 => AssetStatus.LoanedOutInternal,
        5 => AssetStatus.LoanedOutExternal,
        6 => AssetStatus.Discarded,
        14 => AssetStatus.RecalledToStorage,
        15 => AssetStatus.UnderProcurement,
        _ => AssetStatus.RegisteredUnassigned
    };

    private async Task ImportRfidTagsAsync(List<JsonElement> tagFile, Dictionary<int, ImportedAsset> assets, string operatorName, FamasImportResult result, CancellationToken ct)
    {
        var collection = db.RfidTags.Collection;
        var existing = await collection.Find(FilterDefinition<RfidTag>.Empty).ToListAsync(ct);
        var byCode = existing.GroupBy(t => t.RfidCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var assetsWithTag = existing.Where(t => t.AssetId.HasValue).Select(t => t.AssetId!.Value).ToHashSet();
        int added = 0, updated = 0, skipped = 0;

        var candidates = tagFile
            .Select(r => (Row: r, Epc: Str(r, "RFID_EPC_CODE"), LegacyId: Int(r, "id") ?? 0))
            .Where(x => x.Epc.Length > 0 && assets.ContainsKey(x.LegacyId))
            .ToList();
        var ids = await PoolAsync("rfidTags", candidates.Count(x => !byCode.ContainsKey(x.Epc)), ct);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newTags = new List<RfidTag>();

        foreach (var (row, epc, legacyId) in candidates)
        {
            if (!seen.Add(epc))
            {
                skipped++;
                continue;
            }

            var assetId = assets[legacyId].AppId;
            if (byCode.TryGetValue(epc, out var found))
            {
                if (found.AssetId != assetId && !assetsWithTag.Contains(assetId))
                {
                    await collection.UpdateOneAsync(t => t.Id == found.Id,
                        Builders<RfidTag>.Update.Set(t => t.AssetId, assetId).Set(t => t.TagStatus, RfidTagStatus.Active), cancellationToken: ct);
                    assetsWithTag.Add(assetId);
                }

                updated++;
                continue;
            }

            if (assetsWithTag.Contains(assetId))
            {
                skipped++;
                continue;
            }

            var tag = new RfidTag
            {
                Id = await ids.NextAsync(),
                AssetId = assetId,
                RfidCode = epc,
                TagStatus = Bool(row, "active") ? RfidTagStatus.Active : RfidTagStatus.Retired,
                IsActive = Bool(row, "active"),
                EncodedBy = NullIfEmpty(Str(row, "Print_by")) ?? operatorName,
                EncodedAt = Date(row, "Print_date"),
                RegisteredAt = Date(row, "generated_date") ?? DateTime.UtcNow
            };
            newTags.Add(tag);
            assetsWithTag.Add(assetId);
            added++;
        }

        foreach (var chunk in newTags.Chunk(1000))
        {
            await collection.InsertManyAsync(chunk, cancellationToken: ct);
        }

        result.Mapped["RFID tags"] = (added, updated);
        if (skipped > 0)
        {
            result.Warnings.Add($"{skipped} RFID code(s) were shared by more than one asset in FAMAS. Each code was linked to the first asset only.");
        }
    }

    // ---------------------------------------------------------------- history

    private async Task ImportTransfersAsync(List<JsonElement> rows, Dictionary<int, ImportedAsset> assets,
        Dictionary<int, int> departments, Dictionary<int, int> locations, FamasImportResult result, CancellationToken ct)
    {
        var legacyMap = await LoadMapAsync("transfer", ct);
        var fresh = rows.Where(r => !legacyMap.ContainsKey(Int(r, "id") ?? 0)).ToList();
        var ids = await PoolAsync("assetTransfers", fresh.Count, ct);
        var docs = new List<AssetTransfer>();
        var mapEntries = new List<(string Key, int AppId)>();

        foreach (var row in fresh)
        {
            var legacyId = Int(row, "id") ?? 0;
            if (!assets.TryGetValue(Int(row, "refid") ?? 0, out var asset)) continue;
            var when = Date(row, "odate") ?? DateTime.UtcNow;
            var transfer = new AssetTransfer
            {
                Id = await ids.NextAsync(),
                AssetId = asset.AppId,
                FromDepartmentId = departments.GetValueOrDefault(Int(row, "from_deptid") ?? 0),
                ToDepartmentId = departments.GetValueOrDefault(Int(row, "to_deptid") ?? 0),
                FromLocationId = locations.TryGetValue(Int(row, "from_locationid") ?? 0, out var fl) ? fl : null,
                ToLocationId = locations.TryGetValue(Int(row, "to_locationid") ?? 0, out var tl) ? tl : null,
                Status = "Completed",
                RequestedBy = Str(row, "Operator"),
                ApprovedBy = NullIfEmpty(Str(row, "receiveby")),
                ApprovedAt = when,
                CreatedAt = when
            };
            docs.Add(transfer);
            mapEntries.Add(($"transfer:{legacyId}", transfer.Id));
        }

        if (docs.Count > 0) await db.AssetTransfers.Collection.InsertManyAsync(docs, cancellationToken: ct);
        await SaveMapAsync(mapEntries, ct);
        result.Mapped["Transfers (history)"] = (docs.Count, rows.Count - fresh.Count);
    }

    private async Task ImportOperationLogAsync(List<JsonElement> rows, FamasImportResult result, CancellationToken ct)
    {
        var legacyMap = await LoadMapAsync("oplog", ct);
        var fresh = rows.Where(r => !legacyMap.ContainsKey(Int(r, "id") ?? 0)).ToList();
        var ids = await PoolAsync("systemAuditLogs", fresh.Count, ct);
        var docs = new List<SystemAuditLog>();
        var mapEntries = new List<(string Key, int AppId)>();

        foreach (var row in fresh)
        {
            var log = new SystemAuditLog
            {
                Id = await ids.NextAsync(),
                Username = FirstNonEmpty(Str(row, "Operator"), "FAMAS"),
                Action = Str(row, "Detail"),
                EntityType = "Asset (FAMAS)",
                EntityId = NullIfEmpty(Str(row, "Tagcode")),
                OccurredAt = CombineDateTime(Date(row, "LogDate"), Str(row, "LogTime")) ?? DateTime.UtcNow
            };
            docs.Add(log);
            mapEntries.Add(($"oplog:{Int(row, "id") ?? 0}", log.Id));
        }

        foreach (var chunk in docs.Chunk(1000))
        {
            await db.SystemAuditLogs.Collection.InsertManyAsync(chunk, cancellationToken: ct);
        }

        await SaveMapAsync(mapEntries, ct);
        result.Mapped["Activity log entries"] = (docs.Count, rows.Count - fresh.Count);
    }

    // ---------------------------------------------------------------- id + map helpers

    /// <summary>Hands out reserved ids; asks the counter for more if the reserved block runs out.</summary>
    private sealed class IdPool(int[] reserved, Func<Task<int>> fallback)
    {
        private int index;

        public async Task<int> NextAsync() => index < reserved.Length ? reserved[index++] : await fallback();
    }

    private async Task<IdPool> PoolAsync(string sequence, int expected, CancellationToken ct)
        => new IdPool(await ReserveIdsAsync(sequence, expected, ct), () => db.NextIdAsync(sequence, ct));

    /// <summary>Reserves a block of ids from the app's "counters" collection in one call.</summary>
    private async Task<int[]> ReserveIdsAsync(string sequence, int count, CancellationToken ct)
    {
        if (count <= 0) return [];
        var counters = db.Database.GetCollection<BsonDocument>("counters");
        var doc = await counters.FindOneAndUpdateAsync(
            Builders<BsonDocument>.Filter.Eq("_id", sequence),
            Builders<BsonDocument>.Update.Inc("Seq", count),
            new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
            ct);
        var last = doc["Seq"].ToInt32();
        return Enumerable.Range(last - count + 1, count).ToArray();
    }

    private async Task<Dictionary<int, int>> LoadMapAsync(string kind, CancellationToken ct)
    {
        var collection = db.Database.GetCollection<BsonDocument>(MapCollection);
        var docs = await collection.Find(Builders<BsonDocument>.Filter.Regex("_id", new BsonRegularExpression($"^{kind}:"))).ToListAsync(ct);
        var map = new Dictionary<int, int>();
        foreach (var d in docs)
        {
            var key = d["_id"].AsString;
            if (int.TryParse(key.AsSpan(kind.Length + 1), out var legacyId)) map[legacyId] = d["AppId"].ToInt32();
        }

        return map;
    }

    private async Task SaveMapAsync(List<(string Key, int AppId)> entries, CancellationToken ct)
    {
        if (entries.Count == 0) return;
        var collection = db.Database.GetCollection<BsonDocument>(MapCollection);
        var writes = entries.Select(e => new ReplaceOneModel<BsonDocument>(
            Builders<BsonDocument>.Filter.Eq("_id", e.Key),
            new BsonDocument { { "_id", e.Key }, { "AppId", e.AppId } }) { IsUpsert = true }).ToList();
        foreach (var chunk in writes.Chunk(1000))
        {
            await collection.BulkWriteAsync(chunk, new BulkWriteOptions { IsOrdered = false }, ct);
        }
    }

    // ---------------------------------------------------------------- JSON helpers

    private static List<JsonElement> Rows(Dictionary<string, List<JsonElement>> tables, string name)
        => tables.TryGetValue(name, out var rows) ? rows : [];

    private static string NormaliseTableName(string name)
    {
        name = name.Trim().TrimEnd('\\');
        if (name.StartsWith("dbo.", StringComparison.OrdinalIgnoreCase)) name = name[4..];
        if (name.StartsWith("dbo_", StringComparison.OrdinalIgnoreCase)) name = name[4..];
        return Regex.Replace(name, @"[^A-Za-z0-9_]", "");
    }

    private static string Str(JsonElement row, string column)
    {
        if (!row.TryGetProperty(column, out var v)) return string.Empty;
        return v.ValueKind switch
        {
            JsonValueKind.String => (v.GetString() ?? string.Empty).Trim(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static int? Int(JsonElement row, string column)
    {
        if (!row.TryGetProperty(column, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var dbl)) return (int)dbl;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        if (v.ValueKind == JsonValueKind.True) return 1;
        if (v.ValueKind == JsonValueKind.False) return 0;
        return null;
    }

    private static bool Bool(JsonElement row, string column)
    {
        if (!row.TryGetProperty(column, out var v)) return false;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => v.TryGetInt32(out var i) && i != 0,
            JsonValueKind.String => v.GetString() is "1" or "true" or "True",
            _ => false
        };
    }

    private static DateTime? Date(JsonElement row, string column)
    {
        var text = Str(row, column);
        if (text.Length == 0) return null;
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? d
            : null;
    }

    private static DateTime? CombineDateTime(DateTime? date, string time)
    {
        if (date is null) return null;
        return DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? DateTime.SpecifyKind(date.Value.Date + t.TimeOfDay, DateTimeKind.Utc)
            : date;
    }

    private static string Key(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    /// <summary>"HEAD OFFICE - LAGOS HQ" becomes "Head Office - Lagos HQ". Short all-caps words (HQ, IT, AGM) are kept.</summary>
    private static string Title(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var words = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w =>
            w.Length <= 3 && w.All(ch => !char.IsLetter(ch) || char.IsUpper(ch)) ? w
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w.ToLowerInvariant())));
    }

    [GeneratedRegex(@"(""table""\s*:\s*""[^""\\]*)\\("",)")]
    private static partial Regex StrayBackslashInTableName();
}
