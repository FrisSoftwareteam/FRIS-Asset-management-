using System.Text.RegularExpressions;
using FrislEams.Web.Data;
using FrislEams.Web.Domain;
using FrislEams.Web.Models;
using FrislEams.Web.Services;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FrislEams.Web.Controllers.Api;

/// <summary>
/// Endpoints for the FRISL Tracker mobile app. Sign in with POST /api/auth/login, then send
/// "Authorization: Bearer &lt;token&gt;" on every other call.
/// </summary>
[ApiController]
public class MobileApiController(
    AppDbContext db,
    ApiTokenService tokens,
    StockVerificationService stockVerification,
    AuditScanService auditScans,
    RoleGuard roleGuard) : ControllerBase
{
    public sealed record LoginRequest(string Username, string Password, string? Device);
    public sealed record ResolveRequest(List<string> Codes);
    public sealed record StartVerificationRequest(int? DepartmentId, int? LocationId);
    public sealed record ScanRequest(string RfidCode);
    public sealed record ScanBatchRequest(List<string> RfidCodes);
    public sealed record CreatePeriodRequest(string Name, DateTime? EndDate);
    public sealed record UploadRequest(List<string> Codes);

    // ------------------------------------------------------------------ auth

    [HttpPost("/api/auth/login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var user = await tokens.AuthenticateAsync(request.Username, request.Password, ct);
        if (user is null) return Unauthorized(new { error = "Invalid username or password." });

        var (token, expiresAt) = await tokens.IssueAsync(user, request.Device, ct);
        return Ok(new { token, expiresAt, user = UserDto(user) });
    }

    [HttpPost("/api/auth/logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var bearer = ApiTokenService.ReadBearer(Request);
        if (bearer is not null) await tokens.RevokeAsync(bearer, ct);
        return Ok(new { signedOut = true });
    }

    [HttpGet("/api/auth/me")]
    public IActionResult Me()
        => HttpContext.Items[ApiTokenService.ItemKey] is UserAccount user
            ? Ok(UserDto(user))
            : Ok(new { username = HttpContext.Session.GetString("LoginUsername"), displayName = HttpContext.Session.GetString("UserName"), role = HttpContext.Session.GetString("UserRole") });

    // ------------------------------------------------------------------ lookups

    /// <summary>Departments, locations, categories and statuses, for pickers and filters.</summary>
    [HttpGet("/api/lookups")]
    public async Task<IActionResult> Lookups(CancellationToken ct)
    {
        var departments = await db.Departments.Collection.Find(FilterDefinition<Department>.Empty).SortBy(d => d.Name).ToListAsync(ct);
        var locations = await db.Locations.Collection.Find(FilterDefinition<Location>.Empty).SortBy(l => l.Name).ToListAsync(ct);
        var categories = await db.AssetCategories.Collection.Find(FilterDefinition<AssetCategory>.Empty).SortBy(c => c.Name).ToListAsync(ct);
        return Ok(new
        {
            departments = departments.Select(d => new { d.Id, d.Name }),
            locations = locations.Select(l => new { l.Id, l.Name }),
            categories = categories.Select(c => new { c.Id, c.Name }),
            statuses = Enum.GetValues<AssetStatus>().Select(s => new { value = s.ToString(), label = DashText.Humanize(s) })
        });
    }

    // ------------------------------------------------------------------ assets

    /// <summary>Find one asset by RFID code (EPC), tag code (with or without dashes) or serial number.</summary>
    [HttpGet("/api/assets/lookup")]
    public async Task<IActionResult> LookupAsset([FromQuery] string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return BadRequest(new { error = "Pass ?code=<RFID, tag code or serial>." });
        var ctx = await LoadContextAsync(ct);
        var asset = await FindAssetAsync(code.Trim(), ctx, ct);
        return asset is null
            ? NotFound(new { error = "No asset matches that code.", code })
            : Ok(AssetDto(asset, ctx));
    }

    /// <summary>Resolve many scanned codes at once, for example after a bulk RFID read.</summary>
    [HttpPost("/api/assets/resolve")]
    public async Task<IActionResult> Resolve([FromBody] ResolveRequest request, CancellationToken ct)
    {
        var codes = (request.Codes ?? []).Select(c => c?.Trim() ?? "").Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(1000).ToList();
        var ctx = await LoadContextAsync(ct);
        var results = new List<object>();
        var found = 0;
        foreach (var code in codes)
        {
            var asset = await FindAssetAsync(code, ctx, ct);
            if (asset is not null) found++;
            results.Add(new { code, found = asset is not null, asset = asset is null ? null : AssetDto(asset, ctx) });
        }

        return Ok(new { total = codes.Count, found, results });
    }

    /// <summary>Search and page through the register.</summary>
    [HttpGet("/api/assets/search")]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int? departmentId, [FromQuery] int? locationId,
        [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var f = Builders<Asset>.Filter;
        var filter = f.Empty;
        if (departmentId is int d) filter &= f.Eq(a => a.CurrentDepartmentId, d);
        if (locationId is int l) filter &= f.Eq(a => a.CurrentLocationId, l);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AssetStatus>(status, true, out var st)) filter &= f.Eq(a => a.CurrentStatus, st);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var rx = new BsonRegularExpression(Regex.Escape(q.Trim()), "i");
            filter &= f.Or(f.Regex(a => a.TagCode, rx), f.Regex(a => a.AssetName, rx), f.Regex(a => a.SerialNumber, rx), f.Regex(a => a.Brand, rx));
        }

        var total = await db.Assets.Collection.CountDocumentsAsync(filter, cancellationToken: ct);
        var assets = await db.Assets.Collection.Find(filter).SortBy(a => a.TagCode).Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync(ct);
        var ctx = await LoadContextAsync(ct);
        return Ok(new { total, page, pageSize, items = assets.Select(a => AssetDto(a, ctx)) });
    }

    // ------------------------------------------------------------------ stock verification

    [HttpPost("/api/stock-verification/sessions")]
    public async Task<IActionResult> StartVerification([FromBody] StartVerificationRequest request)
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Backoffice, RoleName.Auditor)) return Denied();
        var session = await stockVerification.StartSessionAsync(request.DepartmentId, request.LocationId, roleGuard.GetCurrentUserName(HttpContext));
        return Ok(SessionDto(session));
    }

    [HttpGet("/api/stock-verification/sessions")]
    public async Task<IActionResult> ListVerifications(CancellationToken ct)
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Backoffice, RoleName.Auditor)) return Denied();
        var sessions = await db.StockVerificationSessions.Collection.Find(FilterDefinition<StockVerificationSession>.Empty)
            .SortByDescending(s => s.StartedAt).Limit(30).ToListAsync(ct);
        return Ok(sessions.Select(SessionDto));
    }

    /// <summary>Record one scanned tag in a verification session. Returns Found, Unexpected, Duplicate or Unknown.</summary>
    [HttpPost("/api/stock-verification/sessions/{id:int}/scans")]
    public async Task<IActionResult> Scan(int id, [FromBody] ScanRequest request)
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Backoffice, RoleName.Auditor)) return Denied();
        if (string.IsNullOrWhiteSpace(request.RfidCode)) return BadRequest(new { error = "rfidCode is required." });
        var code = await ToRfidCodeAsync(request.RfidCode.Trim(), HttpContext.RequestAborted);
        var (ok, message, scan) = await stockVerification.ProcessScanAsync(id, code, roleGuard.GetCurrentUserName(HttpContext));
        if (!ok) return BadRequest(new { error = message });
        return Ok(new { message, scan!.Id, scan.RfidCode, scan.AssetId, result = scan.ResultType, scan.ScannedAt });
    }

    /// <summary>Record many scanned tags in one call.</summary>
    [HttpPost("/api/stock-verification/sessions/{id:int}/scans/batch")]
    public async Task<IActionResult> ScanBatch(int id, [FromBody] ScanBatchRequest request)
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Backoffice, RoleName.Auditor)) return Denied();
        var user = roleGuard.GetCurrentUserName(HttpContext);
        var results = new List<object>();
        foreach (var code in (request.RfidCodes ?? []).Select(c => c?.Trim() ?? "").Where(c => c.Length > 0).Take(2000))
        {
            var rfid = await ToRfidCodeAsync(code, HttpContext.RequestAborted);
            var (ok, message, scan) = await stockVerification.ProcessScanAsync(id, rfid, user);
            results.Add(new { rfidCode = code, ok, result = scan?.ResultType, assetId = scan?.AssetId, message });
            if (!ok && message.Contains("not found", StringComparison.OrdinalIgnoreCase)) break;
        }

        var session = await db.StockVerificationSessions.FindAsync(id);
        return Ok(new { session = session is null ? null : SessionDto(session), results });
    }

    [HttpPost("/api/stock-verification/sessions/{id:int}/complete")]
    public async Task<IActionResult> CompleteVerification(int id)
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Backoffice, RoleName.Auditor)) return Denied();
        var session = await stockVerification.CompleteSessionAsync(id, roleGuard.GetCurrentUserName(HttpContext));
        return Ok(SessionDto(session));
    }

    // ------------------------------------------------------------------ audit scan periods

    /// <summary>Audit scan periods the app can upload scans to (POST /api/audit/periods/{id}/scans/{n}).</summary>
    [HttpGet("/api/audit/periods")]
    public async Task<IActionResult> AuditPeriods()
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Auditor, RoleName.Backoffice)) return Denied();
        var periods = await auditScans.GetPeriodsAsync(30);
        return Ok(periods.Select(p => new { p.Id, p.Name, p.Status, p.StartDate, p.EndDate, p.Auditor, p.DistinctScannedCount, p.DiscrepancyCount }));
    }

    [HttpPost("/api/audit/periods")]
    public async Task<IActionResult> CreateAuditPeriod([FromBody] CreatePeriodRequest request)
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Auditor)) return Denied();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "Give the audit period a name." });
        var start = DateTime.UtcNow.Date;
        var end = (request.EndDate ?? start.AddDays(30)).Date;
        if (end < start) return BadRequest(new { error = "The end date can't be in the past." });
        var period = await auditScans.CreatePeriodAsync(request.Name, start, end, roleGuard.GetCurrentUserName(HttpContext));
        return Ok(new { period.Id, period.Name, period.Status, period.StartDate, period.EndDate, period.Auditor });
    }

    /// <summary>
    /// Upload a batch of scanned codes as the next scan in an audit period (the scan number is
    /// picked automatically). Barcodes and typed tag codes are mapped to RFID codes first.
    /// </summary>
    [HttpPost("/api/audit/periods/{id:int}/upload")]
    public async Task<IActionResult> UploadAuditBatch(int id, [FromBody] UploadRequest request, CancellationToken ct)
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Auditor)) return Denied();
        var raw = (request.Codes ?? []).Select(c => c?.Trim() ?? "").Where(c => c.Length > 0).Take(5000).ToList();
        if (raw.Count == 0) return BadRequest(new { error = "Scan at least one tag before uploading." });

        var codes = new List<string>(raw.Count);
        foreach (var code in raw) codes.Add(await ToRfidCodeAsync(code, ct));

        var existing = await auditScans.GetTemporaryScanListAsync(id);
        var nextNumber = existing.Count == 0 ? 1 : existing.Max(s => s.ScanNumber) + 1;
        var (ok, message, scan) = await auditScans.AddScanBatchAsync(id, nextNumber, codes, "mobile", roleGuard.GetCurrentUserName(HttpContext));
        if (!ok) return BadRequest(new { error = message });

        var known = (await db.RfidTags.Collection.Find(Builders<RfidTag>.Filter.In(t => t.RfidCode, codes.Distinct())).ToListAsync(ct))
            .Count(t => t.AssetId.HasValue);
        return Ok(new { message, scanNumber = scan!.ScanNumber, itemCount = scan.ItemCount, distinct = codes.Distinct(StringComparer.OrdinalIgnoreCase).Count(), recognised = known, scan.DuplicateInBatchCount });
    }

    /// <summary>Compare everything scanned in the period with the register and list the concerns.</summary>
    [HttpPost("/api/audit/periods/{id:int}/check")]
    public async Task<IActionResult> CheckAuditPeriod(int id)
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Auditor)) return Denied();
        try
        {
            var result = await auditScans.CheckDiscrepanciesAsync(id, roleGuard.GetCurrentUserName(HttpContext));
            return Ok(new
            {
                periodId = id,
                result.DistinctScannedCount,
                result.TotalScanBatches,
                concernCount = result.Concerns.Count,
                byType = result.Concerns.GroupBy(c => c.Type).Select(g => new { type = g.Key, count = g.Count() }),
                concerns = result.Concerns.Take(200).Select(c => new { c.Type, c.RfidCode, c.AssetTag, c.Message })
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ------------------------------------------------------------------ helpers

    private sealed record LookupContext(
        Dictionary<int, string> Departments, Dictionary<int, string> Locations, Dictionary<int, string> Categories,
        Dictionary<int, string> Staff, Dictionary<int, string> RfidByAsset);

    private async Task<LookupContext> LoadContextAsync(CancellationToken ct)
    {
        var departments = (await db.Departments.Collection.Find(FilterDefinition<Department>.Empty).ToListAsync(ct)).ToDictionary(x => x.Id, x => x.Name);
        var locations = (await db.Locations.Collection.Find(FilterDefinition<Location>.Empty).ToListAsync(ct)).ToDictionary(x => x.Id, x => x.Name);
        var categories = (await db.AssetCategories.Collection.Find(FilterDefinition<AssetCategory>.Empty).ToListAsync(ct)).ToDictionary(x => x.Id, x => x.Name);
        var staff = (await db.Staff.Collection.Find(FilterDefinition<Staff>.Empty).ToListAsync(ct)).ToDictionary(x => x.Id, x => x.FullName);
        var rfid = (await db.RfidTags.Collection.Find(t => t.AssetId != null).ToListAsync(ct))
            .GroupBy(t => t.AssetId!.Value).ToDictionary(g => g.Key, g => g.First().RfidCode);
        return new LookupContext(departments, locations, categories, staff, rfid);
    }

    /// <summary>
    /// Stock checks match on the stored RFID code. A scanned barcode or typed tag code is turned
    /// into that asset's RFID code first, so it counts as Found rather than Unexpected.
    /// </summary>
    private async Task<string> ToRfidCodeAsync(string code, CancellationToken ct)
    {
        foreach (var candidate in CandidateCodes(code))
        {
            var exact = new BsonRegularExpression($"^{Regex.Escape(candidate)}$", "i");
            var tag = await db.RfidTags.Collection.Find(Builders<RfidTag>.Filter.Regex(t => t.RfidCode, exact)).FirstOrDefaultAsync(ct);
            if (tag is not null) return tag.RfidCode;
        }

        var ctx = new LookupContext(new(), new(), new(), new(), new());
        var asset = await FindAssetAsync(code, ctx, ct);
        if (asset is null) return code;
        var assetTag = await db.RfidTags.Collection.Find(t => t.AssetId == asset.Id).FirstOrDefaultAsync(ct);
        return assetTag?.RfidCode ?? code;
    }

    /// <summary>
    /// The forms a scanned value can arrive in. Handheld readers may send the code with
    /// whitespace or control characters, a 96-bit EPC padded with zeros
    /// ("033004000001000000000000"), or the 12-character EPC that FAMAS wrote.
    /// </summary>
    private static List<string> CandidateCodes(string raw)
    {
        var code = Regex.Replace(raw ?? "", @"[\s\x00-\x1F]", "").Trim();
        var list = new List<string>();
        void Add(string c) { if (c.Length > 0 && !list.Contains(c, StringComparer.OrdinalIgnoreCase)) list.Add(c); }
        Add(code);
        if (Regex.IsMatch(code, "^[0-9A-Fa-f]+$"))
        {
            if (code.Length > 12 && code.Length % 4 == 0) Add(code.TrimEnd('0').PadRight(12, '0'));
            if (code.Length >= 24) Add(code[..12]);
        }

        return list;
    }

    private async Task<Asset?> FindAssetAsync(string rawCode, LookupContext ctx, CancellationToken ct)
    {
        var f = Builders<Asset>.Filter;
        foreach (var code in CandidateCodes(rawCode))
        {
            var exact = new BsonRegularExpression($"^{Regex.Escape(code)}$", "i");

            // 1. RFID code (EPC) on a tag.
            var tag = await db.RfidTags.Collection.Find(Builders<RfidTag>.Filter.Regex(t => t.RfidCode, exact)).FirstOrDefaultAsync(ct);
            if (tag?.AssetId is int assetId)
            {
                var byTag = await db.Assets.Collection.Find(a => a.Id == assetId).FirstOrDefaultAsync(ct);
                if (byTag is not null) return byTag;
            }

            // 2. Tag code, serial number or tag number.
            var asset = await db.Assets.Collection.Find(f.Or(f.Regex(a => a.TagCode, exact), f.Regex(a => a.SerialNumber, exact), f.Regex(a => a.TagNumber, exact))).FirstOrDefaultAsync(ct);
            if (asset is not null) return asset;

            // 3. FAMAS EPCs are the tag code without dashes ("033004000001" is "033-004-000001").
            var digits = Regex.Replace(code, "[^0-9]", "");
            if (digits.Length == 12 && digits == code.Replace("-", ""))
            {
                var dashed = $"{digits[..3]}-{digits[3..6]}-{digits[6..]}";
                asset = await db.Assets.Collection.Find(f.Eq(a => a.TagCode, dashed)).FirstOrDefaultAsync(ct);
                if (asset is not null) return asset;
            }
        }

        // 4. The chip's TID (some readers send this instead of the EPC). FAMAS recorded TIDs in its tag file.
        var tidCode = CandidateCodes(rawCode).FirstOrDefault() ?? "";
        if (tidCode.Length >= 16)
        {
            var row = await db.Database.GetCollection<BsonDocument>("famas_TagFile")
                .Find(Builders<BsonDocument>.Filter.Regex("RFID_TID_CODE", new BsonRegularExpression($"^{Regex.Escape(tidCode)}$", "i")))
                .FirstOrDefaultAsync(ct);
            if (row is not null)
            {
                var tagCode = row.GetValue("assigned_tagcode", BsonNull.Value) is { IsString: true } a && a.AsString.Length > 0
                    ? a.AsString
                    : row.GetValue("tagcode", BsonNull.Value) is { IsString: true } t ? t.AsString : null;
                if (tagCode is not null)
                {
                    return await db.Assets.Collection.Find(f.Eq(x => x.TagCode, tagCode)).FirstOrDefaultAsync(ct);
                }
            }
        }

        return null;
    }


    private static object AssetDto(Asset a, LookupContext ctx) => new
    {
        a.Id,
        a.TagCode,
        rfidCode = ctx.RfidByAsset.GetValueOrDefault(a.Id),
        a.AssetName,
        a.Description,
        category = ctx.Categories.GetValueOrDefault(a.AssetCategoryId),
        status = a.CurrentStatus.ToString(),
        statusLabel = DashText.Humanize(a.CurrentStatus),
        condition = a.CurrentCondition,
        departmentId = a.CurrentDepartmentId,
        department = a.CurrentDepartmentId is int d ? ctx.Departments.GetValueOrDefault(d) : null,
        locationId = a.CurrentLocationId,
        location = a.CurrentLocationId is int l ? ctx.Locations.GetValueOrDefault(l) : null,
        custodianId = a.CurrentCustodianId,
        custodian = a.CurrentCustodianId is int s ? ctx.Staff.GetValueOrDefault(s) : null,
        a.Brand,
        a.ModelNumber,
        a.SerialNumber,
        a.PurchaseDate,
        a.PurchaseCost,
        a.WarrantyExpiryDate,
        a.Notes
    };

    private static object SessionDto(StockVerificationSession s) => new
    {
        s.Id, s.Status, s.DepartmentId, s.LocationId, s.InitiatedBy, s.FoundCount, s.MissingCount, s.UnexpectedCount, s.DuplicateCount, s.StartedAt, s.CompletedAt
    };

    private static object UserDto(UserAccount u) => new
    {
        u.Id,
        u.Username,
        displayName = string.IsNullOrWhiteSpace(u.DisplayName) ? u.Username : u.DisplayName,
        u.Role,
        portal = PortalService.GetPortalForRole(u.Role),
        u.MustChangePassword
    };

    private ObjectResult Denied() => StatusCode(StatusCodes.Status403Forbidden, new { error = "Your role is not allowed to do this." });
}
