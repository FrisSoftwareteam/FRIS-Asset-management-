using FrislEams.Web.Domain;
using FrislEams.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace FrislEams.Web.Services;

public class RoleGuard
{
    /// <summary>
    /// True when the signed-in user (mobile bearer token or web session) has one of the roles.
    /// Nobody signed in means no role. The old fallbacks (an X-Role header, a ?role= query value,
    /// or assuming Backoffice) let any caller pick their own role and have been removed.
    /// </summary>
    public bool HasAnyRole(ControllerBase controller, params string[] allowed)
    {
        var role = ResolveRole(controller.HttpContext);
        return role is not null && allowed.Any(a => string.Equals(a, role, StringComparison.OrdinalIgnoreCase));
    }

    public string GetCurrentRole(Microsoft.AspNetCore.Http.HttpContext ctx)
        => ResolveRole(ctx) ?? RoleName.Backoffice;

    /// <summary>Display name of the caller, for audit trails ("System" if unknown).</summary>
    public string GetCurrentUserName(Microsoft.AspNetCore.Http.HttpContext ctx)
        => ctx.Items[ApiTokenService.ItemKey] is UserAccount apiUser
            ? (string.IsNullOrWhiteSpace(apiUser.DisplayName) ? apiUser.Username : apiUser.DisplayName)
            : ctx.Session.GetString("UserName") ?? "System";

    private static string? ResolveRole(Microsoft.AspNetCore.Http.HttpContext ctx)
        => ctx.Items[ApiTokenService.ItemKey] is UserAccount apiUser
            ? apiUser.Role
            : ctx.Session.GetString("UserRole");
}
