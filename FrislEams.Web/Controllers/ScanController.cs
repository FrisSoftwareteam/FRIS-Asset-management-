using FrislEams.Web.Domain;
using FrislEams.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FrislEams.Web.Controllers;

/// <summary>Mobile-first scanning screen (phones and handheld RFID readers). Uses the JSON API.</summary>
public class ScanController(RoleGuard roleGuard) : Controller
{
    [HttpGet("/Scan")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Scan";
        ViewBag.CanStockCheck = roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Backoffice, RoleName.Auditor);
        ViewBag.CanOpenRecord = roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Backoffice, RoleName.Auditor);
        ViewBag.DisplayName = HttpContext.Session.GetString("UserName") ?? "";
        ViewBag.HomePath = PortalService.GetHomePath(roleGuard.GetCurrentRole(HttpContext));
        return View();
    }
}
