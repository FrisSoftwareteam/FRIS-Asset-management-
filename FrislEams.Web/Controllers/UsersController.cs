using FrislEams.Web.Data;
using FrislEams.Web.Domain;
using FrislEams.Web.Models;
using FrislEams.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FrislEams.Web.Controllers;

public class UsersController(AppDbContext db, RoleGuard roleGuard, SystemAuditService audit) : Controller
{
    private static readonly string[] RoleOptions = [RoleName.Admin, RoleName.Backoffice, RoleName.Auditor, RoleName.Staff];

    private bool CanManage() =>
        roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Backoffice);

    [HttpGet]
    public async Task<IActionResult> Index(string? q, string? role, string? status)
    {
        if (!CanManage()) return Forbid();

        var allUsers = await db.UserAccounts.AsQueryable().OrderBy(u => u.DisplayName).ToListAsync();
        ViewBag.AllUsers = allUsers;
        var users = allUsers;
        if (!string.IsNullOrWhiteSpace(role))
        {
            users = users.Where(u => string.Equals(u.Role, role, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (status is "active" or "inactive")
        {
            users = users.Where(u => u.IsActive == (status == "active")).ToList();
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            users = users.Where(u =>
                u.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || u.Username.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (u.EmployeeId?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        ViewBag.RoleOptions = RoleOptions;
        ViewBag.Query = q;
        ViewBag.Role = role;
        ViewBag.Status = status;
        ViewBag.DepartmentNames = (await db.Departments.AsQueryable().ToListAsync()).ToDictionary(d => d.Id, d => d.Name);
        ViewBag.CurrentUsername = HttpContext.Session.GetString("LoginUsername");
        return View(users);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!CanManage()) return Forbid();
        await LoadLookupsAsync();
        return View(new UserCreateVm());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserCreateVm vm)
    {
        if (!CanManage()) return Forbid();
        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync();
            return View(vm);
        }

        var username = vm.Email.Trim().ToLowerInvariant();
        if (await db.UserAccounts.AsQueryable().AnyAsync(u => u.Username == username))
        {
            ModelState.AddModelError(nameof(vm.Email), "A user with this email already exists.");
            await LoadLookupsAsync();
            return View(vm);
        }

        var user = new UserAccount
        {
            Username = username,
            DisplayName = vm.DisplayName.Trim(),
            Password = vm.TemporaryPassword,
            Role = vm.Role,
            EmployeeId = vm.EmployeeId.Trim(),
            Email = vm.Email.Trim(),
            PhoneNumber = vm.PhoneNumber,
            Designation = vm.Designation,
            DepartmentId = vm.DepartmentId,
            BranchLocationId = vm.BranchLocationId,
            MustChangePassword = true,
            IsActive = true
        };
        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();
        await audit.LogAsync(HttpContext.Session.GetString("LoginUsername") ?? "system", "Create User", "UserAccount", user.Id.ToString(), null, user.DisplayName);
        TempData["Success"] = $"User '{user.DisplayName}' created. They must change password on first login.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        if (!CanManage()) return Forbid();
        var user = await db.UserAccounts.FindAsync(id);
        if (user is null) return NotFound();
        if (user.IsActive && string.Equals(user.Username, HttpContext.Session.GetString("LoginUsername"), StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "You can't deactivate the account you're signed in with.";
            return RedirectToAction(nameof(Index));
        }

        user.IsActive = !user.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync(HttpContext.Session.GetString("LoginUsername") ?? "system", user.IsActive ? "Activate User" : "Deactivate User", "UserAccount", id.ToString());
        TempData["Success"] = $"User '{user.DisplayName}' is now {(user.IsActive ? "active" : "inactive")}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int id, string newPassword)
    {
        if (!CanManage()) return Forbid();
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            TempData["Error"] = "Password must be at least 6 characters.";
            return RedirectToAction(nameof(Index));
        }

        var user = await db.UserAccounts.FindAsync(id);
        if (user is null) return NotFound();
        user.Password = newPassword;
        user.MustChangePassword = true;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync(HttpContext.Session.GetString("LoginUsername") ?? "system", "Reset Password", "UserAccount", id.ToString());
        TempData["Success"] = $"Password reset for '{user.DisplayName}'.";
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadLookupsAsync()
    {
        ViewBag.Departments = await db.Departments.AsQueryable().OrderBy(d => d.Name).ToListAsync();
        ViewBag.Branches = await db.Locations.AsQueryable().Where(l => l.LocationType == "Branch").OrderBy(l => l.Name).ToListAsync();
        ViewBag.RoleOptions = RoleOptions;
    }
}
