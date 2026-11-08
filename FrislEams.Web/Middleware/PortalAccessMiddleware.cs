using FrislEams.Web.Services;

namespace FrislEams.Web.Middleware;

public sealed class PortalAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, RoleGuard roleGuard, ApiTokenService apiTokens)
    {
        var path = context.Request.Path.Value ?? "/";
        if (IsStaticAssetPath(path))
        {
            await next(context);
            return;
        }

        // The JSON API (used by the mobile app) signs in with a bearer token instead of the web
        // session, and answers 401 instead of redirecting to the sign-in page.
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            if (path.StartsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            var bearer = ApiTokenService.ReadBearer(context.Request);
            if (bearer is not null)
            {
                var apiUser = await apiTokens.ValidateAsync(bearer, context.RequestAborted);
                if (apiUser is null)
                {
                    await WriteJsonErrorAsync(context, StatusCodes.Status401Unauthorized, "Your sign-in has expired. Please sign in again.");
                    return;
                }

                context.Items[ApiTokenService.ItemKey] = apiUser;
                await next(context);
                return;
            }

            if (string.IsNullOrWhiteSpace(context.Session.GetString("UserName")))
            {
                await WriteJsonErrorAsync(context, StatusCodes.Status401Unauthorized, "Sign in first: POST /api/auth/login, then send the token as 'Authorization: Bearer <token>'.");
                return;
            }

            // Signed in on the web: API controllers check roles themselves, so don't send the
            // browser to a portal page (an HTML redirect breaks fetch calls from the Scan screen).
            await next(context);
            return;
        }

        var role = roleGuard.GetCurrentRole(context);
        var isLoggedIn = !string.IsNullOrWhiteSpace(context.Session.GetString("UserName"));

        if (!isLoggedIn && !IsAnonymousPath(path))
        {
            var returnUrl = string.Equals(path, "/Home/Error", StringComparison.OrdinalIgnoreCase)
                ? "/"
                : path;
            context.Response.Redirect($"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}");
            return;
        }

        if (isLoggedIn && context.Session.GetString("MustChangePassword") == "1"
            && !path.StartsWith("/Account/ChangePassword", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/Account/Logout", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect("/Account/ChangePassword");
            return;
        }

        if (isLoggedIn && !PortalService.CanAccessPath(role, path))
        {
            context.Response.Redirect(PortalService.GetHomePath(role));
            return;
        }

        await next(context);
    }

    private static Task WriteJsonErrorAsync(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error = message });
    }

    private static bool IsStaticAssetPath(string path)
        => path.StartsWith("/css", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/js", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/_", StringComparison.OrdinalIgnoreCase)
           || path.Contains('.', StringComparison.Ordinal);

    private static bool IsAnonymousPath(string path)
        => path.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/Home/Error", StringComparison.OrdinalIgnoreCase);
}
