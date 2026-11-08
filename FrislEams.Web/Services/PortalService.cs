using FrislEams.Web.Domain;

namespace FrislEams.Web.Services;

public static class PortalService
{
    public const string AdminPortal = "Admin";
    public const string BackofficePortal = "Backoffice";
    public const string AuditorPortal = "Auditor";
    public const string StaffPortal = "Staff";

    public static string NormalizeRole(string? role)
        => role?.Trim() ?? string.Empty;

    public static string GetPortalForRole(string? role)
    {
        var normalized = NormalizeRole(role);
        if (string.Equals(normalized, RoleName.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return AdminPortal;
        }

        if (string.Equals(normalized, RoleName.Auditor, StringComparison.OrdinalIgnoreCase))
        {
            return AuditorPortal;
        }

        if (string.Equals(normalized, RoleName.Staff, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, RoleName.DepartmentHead, StringComparison.OrdinalIgnoreCase))
        {
            return StaffPortal;
        }

        return BackofficePortal;
    }

    public static string GetHomePath(string? role) => GetPortalForRole(role) switch
    {
        AdminPortal => "/Portal/Admin",
        AuditorPortal => "/Portal/Auditor",
        StaffPortal => "/Portal/Staff",
        _ => "/Portal/Backoffice"
    };

    public static string GetLoginPath(string portal) => NormalizePortal(portal) switch
    {
        AdminPortal => "/Account/Login/Admin",
        AuditorPortal => "/Account/Login/Auditor",
        StaffPortal => "/Account/Login/Staff",
        _ => "/Account/Login/Backoffice"
    };

    public static string GetDemoUsername(string portal) => NormalizePortal(portal) switch
    {
        AdminPortal => "Admin",
        AuditorPortal => "Auditor",
        StaffPortal => "Staff",
        _ => "Washington"
    };

    public static string NormalizePortal(string? portal)
    {
        if (string.Equals(portal, AdminPortal, StringComparison.OrdinalIgnoreCase))
        {
            return AdminPortal;
        }

        if (string.Equals(portal, AuditorPortal, StringComparison.OrdinalIgnoreCase))
        {
            return AuditorPortal;
        }

        if (string.Equals(portal, StaffPortal, StringComparison.OrdinalIgnoreCase))
        {
            return StaffPortal;
        }

        return BackofficePortal;
    }

    public static bool IsKnownPortal(string? portal)
        => string.Equals(portal, AdminPortal, StringComparison.OrdinalIgnoreCase)
           || string.Equals(portal, AuditorPortal, StringComparison.OrdinalIgnoreCase)
           || string.Equals(portal, BackofficePortal, StringComparison.OrdinalIgnoreCase)
           || string.Equals(portal, StaffPortal, StringComparison.OrdinalIgnoreCase);

    public static bool RoleMatchesPortal(string? role, string portal)
        => string.Equals(GetPortalForRole(role), portal, StringComparison.OrdinalIgnoreCase);

    public static bool CanAccessPath(string? role, string path)
    {
        var portal = GetPortalForRole(role);
        path = path.ToLowerInvariant();

        if (path.StartsWith("/account/login")
            || path.StartsWith("/account/logout")
            || path.StartsWith("/account/profile")
            || path.StartsWith("/account/changepassword")
            || path == "/"
            || path.StartsWith("/home/error"))
        {
            return true;
        }

        if (path.StartsWith("/swagger"))
        {
            return true;
        }

        if (path.StartsWith("/portal/"))
        {
            return path.StartsWith($"/portal/{portal.ToLowerInvariant()}");
        }

        if (path.StartsWith("/notifications"))
        {
            return true;
        }

        if (path.StartsWith("/reports"))
        {
            return PortalCapabilities.CanViewReports(portal);
        }

        if (path.StartsWith("/myassets"))
        {
            return PortalCapabilities.CanViewAssets(portal);
        }

        if (path.StartsWith("/assets"))
        {
            if (path.Contains("/register") || path.Contains("/changestatus"))
            {
                return PortalCapabilities.CanCreateEditAssets(portal);
            }

            return PortalCapabilities.CanViewAssets(portal);
        }

        if (path.StartsWith("/assignments"))
        {
            return PortalCapabilities.CanAssignMoveAssets(portal);
        }

        if (path.StartsWith("/movements") || path.StartsWith("/disposals"))
        {
            return PortalCapabilities.CanManageOperations(portal);
        }

        if (path.StartsWith("/reconciliation") || path.StartsWith("/exceptions"))
        {
            return PortalCapabilities.CanReconcile(portal);
        }

        if (path.StartsWith("/audittrails"))
        {
            return PortalCapabilities.CanViewAuditLogs(portal);
        }

        if (path.StartsWith("/audit"))
        {
            return portal is BackofficePortal or AdminPortal or AuditorPortal;
        }

        if (path.StartsWith("/workflow"))
        {
            return true;
        }

        if (path.StartsWith("/rfidreader"))
        {
            return PortalCapabilities.CanScanRfid(portal);
        }

        if (path.StartsWith("/rfidtags"))
        {
            return PortalCapabilities.CanViewRfidTags(portal);
        }

        if (path.StartsWith("/stockverification"))
        {
            return PortalCapabilities.CanScanRfid(portal);
        }

        if (path.StartsWith("/auditscan"))
        {
            return PortalCapabilities.CanScanRfid(portal);
        }

        if (path.StartsWith("/api/audit"))
        {
            return PortalCapabilities.CanScanRfid(portal);
        }

        if (path.StartsWith("/transfers"))
        {
            return PortalCapabilities.CanAssignMoveAssets(portal);
        }

        if (path.StartsWith("/masterdata"))
        {
            return PortalCapabilities.CanManageOperations(portal);
        }

        if (path.StartsWith("/users"))
        {
            return PortalCapabilities.CanManageUsers(portal);
        }

        if (path.StartsWith("/userroles")
            || path.StartsWith("/vendors")
            || path.StartsWith("/contractors")
            || path.StartsWith("/staff"))
        {
            return PortalCapabilities.CanManageUsers(portal);
        }

        if (path.StartsWith("/dataimport"))
        {
            return PortalCapabilities.CanManageSystemSettings(portal);
        }

        if (path.StartsWith("/settings"))
        {
            return portal is BackofficePortal or AdminPortal;
        }

        if (path.StartsWith("/dashboard"))
        {
            return PortalCapabilities.CanManageSystemSettings(portal);
        }

        return portal switch
        {
            AdminPortal => true,
            BackofficePortal => true,
            _ => false
        };
    }
}
