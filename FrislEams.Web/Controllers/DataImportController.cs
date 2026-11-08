using FrislEams.Web.Data;
using FrislEams.Web.Services;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FrislEams.Web.Controllers;

/// <summary>Admin page for importing the legacy FAMAS JSON export into MongoDB.</summary>
public class DataImportController(AppDbContext db, FamasImportService importer, SampleDataCleanupService samples, RoleGuard roleGuard) : Controller
{
    private const long MaxUploadBytes = 100L * 1024 * 1024;

    [HttpGet("/DataImport")]
    public async Task<IActionResult> Index()
    {
        if (!IsAdmin()) return Redirect(PortalService.GetHomePath(roleGuard.GetCurrentRole(HttpContext)));

        ViewData["Title"] = "Data import";
        ViewBag.Runs = await LoadRunsAsync();
        ViewBag.SamplePreview = await samples.RunAsync(apply: false);
        return View();
    }

    [HttpPost("/DataImport/RemoveSamples")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveSamples()
    {
        if (!IsAdmin()) return Redirect(PortalService.GetHomePath(roleGuard.GetCurrentRole(HttpContext)));

        var removed = await samples.RunAsync(apply: true);
        var total = removed.Sum(r => r.Count);
        TempData["Success"] = total == 0
            ? "There was no sample data left to remove."
            : $"Removed {total:N0} sample records: " + string.Join(", ", removed.Where(r => r.Count > 0).Select(r => $"{r.Label.ToLowerInvariant()} ({r.Count})")) + ".";
        return Redirect("/DataImport");
    }

    [HttpPost("/DataImport/Famas")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Famas(List<IFormFile> files, CancellationToken cancellationToken)
    {
        if (!IsAdmin()) return Redirect(PortalService.GetHomePath(roleGuard.GetCurrentRole(HttpContext)));

        ViewData["Title"] = "Data import";
        var result = new FamasImportResult();
        var jsonFiles = (files ?? [])
            .Where(f => f.Length > 0 && f.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (jsonFiles.Count == 0)
        {
            result.Warnings.Add("Choose the _all_tables.json file, or the dbo_*.json files, from the FAMAS export.");
        }
        else
        {
            var contents = new List<(string FileName, string Content)>();
            foreach (var file in jsonFiles)
            {
                using var reader = new StreamReader(file.OpenReadStream());
                contents.Add((file.FileName, await reader.ReadToEndAsync(cancellationToken)));
            }

            var tables = FamasImportService.ParseFiles(contents, result);
            var operatorName = HttpContext.Session.GetString("UserName") ?? "Admin";
            // Not tied to the request: if the browser gives up waiting, the import still finishes.
            await importer.ImportAsync(tables, operatorName, result, CancellationToken.None);
        }

        ViewBag.Result = result;
        ViewBag.Runs = await LoadRunsAsync();
        ViewBag.SamplePreview = await samples.RunAsync(apply: false);
        return View("Index");
    }

    private bool IsAdmin()
        => PortalService.GetPortalForRole(roleGuard.GetCurrentRole(HttpContext)) == PortalService.AdminPortal;

    private async Task<List<BsonDocument>> LoadRunsAsync()
        => await db.Database.GetCollection<BsonDocument>("famas_import_runs")
            .Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending("RunAt"))
            .Limit(5)
            .ToListAsync();
}
