using FrislEams.Web.Data;
using FrislEams.Web.Models;
using FrislEams.Web.Services;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace FrislEams.Web.Controllers;

public class AccountController(AppDbContext db) : Controller
{
    [HttpGet("/Account/Login")]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View("Login");
    }

    [HttpPost("/Account/Login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
    {
        var user = await TryAuthenticateAsync(username, password);
        if (user is not null)
        {
            return await SignInUserAsync(user, returnUrl);
        }

        ViewBag.ReturnUrl = returnUrl;
        ViewBag.Error = "Invalid username or password. Please try again.";
        return View("Login");
    }

    [HttpGet("/Account/Login/{portal}")]
    public IActionResult LoginPortal(string portal, string? returnUrl = null)
    {
        if (!PortalService.IsKnownPortal(portal))
        {
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        ViewBag.ReturnUrl = returnUrl;
        ViewBag.Portal = PortalService.NormalizePortal(portal);
        ViewBag.DemoUser = PortalService.GetDemoUsername(portal);
        return View("LoginPortal");
    }

    [HttpPost("/Account/Login/{portal}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginPortal(string portal, string username, string password, string? returnUrl = null)
    {
        if (!PortalService.IsKnownPortal(portal))
        {
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        var user = await TryAuthenticateAsync(username, password);
        if (user is not null)
        {
            return await SignInUserAsync(user, returnUrl);
        }

        ViewBag.ReturnUrl = returnUrl;
        ViewBag.Portal = PortalService.NormalizePortal(portal);
        ViewBag.DemoUser = PortalService.GetDemoUsername(portal);
        ViewBag.Error = "Invalid username or password. Please try again.";
        return View("LoginPortal");
    }

    [HttpGet("/Account/Logout")]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("/Account/Profile")]
    public IActionResult Profile()
    {
        ViewData["Title"] = "Profile";
        ViewBag.DisplayName = HttpContext.Session.GetString("UserName");
        ViewBag.Username = HttpContext.Session.GetString("LoginUsername");
        ViewBag.Role = HttpContext.Session.GetString("UserRole");
        ViewBag.Portal = HttpContext.Session.GetString("UserPortal");
        return View();
    }

    private async Task<Models.UserAccount?> TryAuthenticateAsync(string? username, string? password)
    {
        var normalizedUsername = username?.Trim().ToLower();
        if (string.IsNullOrWhiteSpace(normalizedUsername) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var user = await UserAccountRepository.FindByUsernameAsync(db.UserAccounts.Collection, normalizedUsername);
        if (user is null || !user.IsActive || user.Password != password)
        {
            return null;
        }

        return user;
    }

    private async Task<IActionResult> SignInUserAsync(Models.UserAccount user, string? returnUrl)
    {
        var portal = PortalService.GetPortalForRole(user.Role);
        HttpContext.Session.SetString("UserName", user.DisplayName);
        HttpContext.Session.SetString("LoginUsername", user.Username);
        HttpContext.Session.SetString("UserRole", user.Role);
        HttpContext.Session.SetString("UserPortal", portal);
        HttpContext.Session.SetString("MustChangePassword", user.MustChangePassword ? "1" : "0");
        await HttpContext.Session.CommitAsync();
        TempData["Success"] = $"Welcome to the {portal} portal, {user.DisplayName}!";

        if (user.MustChangePassword)
        {
            return RedirectToAction(nameof(ChangePassword));
        }

        if (string.IsNullOrWhiteSpace(returnUrl) || returnUrl == "/")
        {
            // Auditors sign in on a phone to scan, so send them straight to the scanner there.
            var agent = Request.Headers.UserAgent.ToString();
            var onPhone = agent.Contains("Mobi", StringComparison.OrdinalIgnoreCase) || agent.Contains("Android", StringComparison.OrdinalIgnoreCase);
            if (onPhone && string.Equals(portal, PortalService.AuditorPortal, StringComparison.OrdinalIgnoreCase))
            {
                return Redirect("/Scan");
            }
        }

        return Redirect(string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl) ? PortalService.GetHomePath(user.Role) : returnUrl);
    }

    [HttpGet("/Account/ChangePassword")]
    public IActionResult ChangePassword()
    {
        ViewData["Title"] = "Change Password";
        return View(new ChangePasswordVm());
    }

    [HttpPost("/Account/ChangePassword")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var username = HttpContext.Session.GetString("LoginUsername");
        if (string.IsNullOrWhiteSpace(username)) return RedirectToAction(nameof(Login));

        var user = await db.UserAccounts.Collection
            .Find(UserAccountRepository.CaseInsensitiveUsernameFilter(username))
            .FirstOrDefaultAsync();
        if (user is null || user.Password != vm.CurrentPassword)
        {
            ModelState.AddModelError(string.Empty, "Current password is incorrect.");
            return View(vm);
        }

        user.Password = vm.NewPassword;
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        db.UserAccounts.Update(user);
        await db.SaveChangesAsync();
        HttpContext.Session.SetString("MustChangePassword", "0");
        TempData["Success"] = "Password updated successfully.";
        return Redirect(PortalService.GetHomePath(user.Role));
    }
}
