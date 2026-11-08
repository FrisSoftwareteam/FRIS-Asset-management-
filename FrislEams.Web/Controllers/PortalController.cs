using FrislEams.Web.Data;
using FrislEams.Web.Domain;
using FrislEams.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FrislEams.Web.Controllers;

public class PortalController(AppDbContext db, DashboardService dashboardService, FeatureHubService hub, RoleGuard roleGuard) : Controller
{
    [HttpGet("/Portal/Admin")]
    public async Task<IActionResult> Admin()
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Admin))
        {
            return Redirect(PortalService.GetHomePath(roleGuard.GetCurrentRole(HttpContext)));
        }

        ViewData["PortalName"] = PortalService.AdminPortal;
        ViewBag.Metrics = await dashboardService.GetAdminPortalMetricsAsync();
        ViewBag.Summary = await dashboardService.GetAdminMetricsFullAsync();
        var users = await db.UserAccounts.AsQueryable().ToListAsync();
        ViewBag.RecentUsers = users.OrderByDescending(u => u.CreatedAt).Take(5).ToList();
        ViewBag.UsersByRole = users
            .GroupBy(u => string.IsNullOrWhiteSpace(u.Role) ? "Unassigned" : u.Role)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        // Real activity for the last 7 days, taken from the system audit log.
        var today = DateTime.UtcNow.Date;
        var since = today.AddDays(-6);
        var recentLogs = await db.SystemAuditLogs.AsQueryable()
            .Where(l => l.OccurredAt >= since)
            .ToListAsync();
        ViewBag.ActivityDays = Enumerable.Range(0, 7).Select(i => since.AddDays(i)).ToArray();
        ViewBag.UserActivity = Enumerable.Range(0, 7)
            .Select(i => recentLogs.Count(l => l.OccurredAt.Date == since.AddDays(i)))
            .ToArray();
        ViewBag.RecentLogs = await db.SystemAuditLogs.AsQueryable()
            .OrderByDescending(l => l.OccurredAt)
            .Take(6)
            .ToListAsync();
        return View("Admin");
    }

    [HttpGet("/Portal/Backoffice")]
    public async Task<IActionResult> Backoffice()
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Backoffice))
        {
            return Redirect(PortalService.GetHomePath(roleGuard.GetCurrentRole(HttpContext)));
        }

        ViewData["PortalName"] = PortalService.BackofficePortal;
        ViewBag.ManualMetrics = await dashboardService.GetBackofficeManualMetricsAsync();
        ViewBag.RecentMovements = await hub.GetRecentMovementsAsync(8);
        return View("Backoffice", await dashboardService.GetBackofficeMetricsAsync());
    }

    [HttpGet("/Portal/Auditor")]
    public async Task<IActionResult> Auditor()
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Auditor))
        {
            return Redirect(PortalService.GetHomePath(roleGuard.GetCurrentRole(HttpContext)));
        }

        ViewData["PortalName"] = PortalService.AuditorPortal;
        ViewBag.Metrics = await dashboardService.GetAuditorPortalMetricsAsync();
        ViewBag.Reconciliation = await hub.GetReconciliationSummaryAsync();
        ViewBag.Exceptions = await hub.GetExceptionsAsync();
        ViewBag.AuditTrails = await db.AssetStatusHistories.AsQueryable().OrderByDescending(h => h.ChangedAt).Take(8).ToListAsync();
        return View();
    }

    [HttpGet("/Portal/Staff")]
    public async Task<IActionResult> Staff()
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Staff, RoleName.DepartmentHead))
        {
            return Redirect(PortalService.GetHomePath(roleGuard.GetCurrentRole(HttpContext)));
        }

        ViewData["PortalName"] = PortalService.StaffPortal;
        ViewBag.Metrics = await dashboardService.GetStaffPortalMetricsAsync(
            HttpContext.Session.GetString("LoginUsername"),
            HttpContext.Session.GetString("UserName"));
        ViewBag.MyAssets = await hub.GetStaffAssetsAsync(
            HttpContext.Session.GetString("LoginUsername"),
            HttpContext.Session.GetString("UserName"));
        return View();
    }
}
