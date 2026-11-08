using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;

namespace FrislEams.Web.Services;

/// <summary>A link in a portal sidebar. <paramref name="Match"/> is the path prefix that marks it active.</summary>
public sealed record NavLink(string Label, string Href, string Match, string Icon, string? Exclude = null);

/// <summary>A titled group of sidebar links.</summary>
public sealed record NavGroup(string Title, NavLink[] Items);

/// <summary>Sidebar model rendered by Views/Shared/_Sidebars/_PortalNav.cshtml.</summary>
public sealed record PortalNavModel(string Portal, string Title, string Subtitle, string BrandIcon, NavGroup[] Groups);

/// <summary>Inline line icons (24x24, drawn with stroke = currentColor) shared by the portal UIs.</summary>
public static class UiIcons
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["dashboard"] = "<rect x='3' y='3' width='7' height='9' rx='1'/><rect x='14' y='3' width='7' height='5' rx='1'/><rect x='14' y='12' width='7' height='9' rx='1'/><rect x='3' y='16' width='7' height='5' rx='1'/>",
        ["bell"] = "<path d='M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9'/><path d='M13.73 21a2 2 0 0 1-3.46 0'/>",
        ["box"] = "<path d='M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z'/><path d='M3.27 6.96 12 12.01l8.73-5.05M12 22.08V12'/>",
        ["assign"] = "<path d='M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2'/><circle cx='9' cy='7' r='4'/><path d='m16 11 2 2 4-4'/>",
        ["transfer"] = "<path d='m17 3 4 4-4 4'/><path d='M3 7h18'/><path d='m7 21-4-4 4-4'/><path d='M21 17H3'/>",
        ["trash"] = "<path d='M3 6h18'/><path d='M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2'/>",
        ["inbox"] = "<path d='M22 12h-6l-2 3h-4l-2-3H2'/><path d='M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z'/>",
        ["wrench"] = "<path d='M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z'/>",
        ["clock"] = "<circle cx='12' cy='12' r='10'/><path d='M12 6v6l4 2'/>",
        ["tag"] = "<path d='M20.59 13.41 13.42 20.58a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z'/><circle cx='7' cy='7' r='1.5'/>",
        ["wifi"] = "<path d='M5 12.55a11 11 0 0 1 14.08 0M1.42 9a16 16 0 0 1 21.16 0M8.53 16.11a6 6 0 0 1 6.95 0'/><circle cx='12' cy='20' r='1'/>",
        ["scale"] = "<path d='M12 3v18M5 21h14'/><path d='m3 9 3-6 3 6a3 3 0 0 1-6 0zM15 9l3-6 3 6a3 3 0 0 1-6 0z'/><path d='M6 3h12'/>",
        ["chart"] = "<path d='M3 3v18h18'/><path d='M7 15l4-4 3 3 5-6'/>",
        ["users"] = "<path d='M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2'/><circle cx='9' cy='7' r='4'/><path d='M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75'/>",
        ["shield"] = "<path d='M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z'/>",
        ["id"] = "<rect x='2' y='5' width='20' height='14' rx='2'/><circle cx='8' cy='12' r='2'/><path d='M14 10h4M14 14h4'/>",
        ["truck"] = "<path d='M1 3h15v13H1zM16 8h4l3 3v5h-7z'/><circle cx='5.5' cy='18.5' r='2.5'/><circle cx='18.5' cy='18.5' r='2.5'/>",
        ["layers"] = "<path d='m12 2 10 5-10 5L2 7z'/><path d='m2 17 10 5 10-5M2 12l10 5 10-5'/>",
        ["gear"] = "<circle cx='12' cy='12' r='3'/><path d='M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z'/>",
        ["log"] = "<path d='M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z'/><path d='M14 2v6h6M8 13h8M8 17h5'/>",
        ["plus"] = "<path d='M12 5v14M5 12h14'/>",
        ["scan"] = "<path d='M3 7V5a2 2 0 0 1 2-2h2M17 3h2a2 2 0 0 1 2 2v2M21 17v2a2 2 0 0 1-2 2h-2M7 21H5a2 2 0 0 1-2-2v-2'/><path d='M7 12h10'/>",
        ["check"] = "<path d='M9 11l3 3L22 4'/><path d='M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11'/>",
        ["calendar"] = "<rect x='3' y='4' width='18' height='18' rx='2'/><path d='M16 2v4M8 2v4M3 10h18'/>",
        ["alert"] = "<path d='M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z'/><path d='M12 9v4M12 17h.01'/>",
        ["search"] = "<circle cx='11' cy='11' r='8'/><path d='m21 21-4.35-4.35'/>",
        ["list"] = "<path d='M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01'/>",
        ["briefcase"] = "<rect x='2' y='7' width='20' height='14' rx='2'/><path d='M16 21V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16'/>",
        ["eye"] = "<path d='M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z'/><circle cx='12' cy='12' r='3'/>",
    };

    public static IHtmlContent Svg(string name, string cssClass = "dash-svg")
        => new HtmlString($"<svg class=\"{cssClass}\" viewBox=\"0 0 24 24\" aria-hidden=\"true\">{(Paths.TryGetValue(name, out var p) ? p : string.Empty)}</svg>");
}

/// <summary>Small text helpers for dashboards.</summary>
public static partial class DashText
{
    public static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} h ago";
        if (span.TotalDays < 2) return "yesterday";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays} days ago";
        return utc.ToString("d MMM yyyy");
    }

    public static string Initials(string? name)
    {
        var parts = (name ?? string.Empty).Split(new[] { ' ', '.', '_', '@' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    /// <summary>"ActiveAssigned" becomes "Active assigned".</summary>
    public static string Humanize(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        if (text.Length == 0) return text;
        var spaced = SplitWords().Replace(text, " $1").Trim();
        return char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant();
    }

    public static string Greeting()
    {
        var hour = DateTime.Now.Hour;
        return hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
    }

    public static string Pct(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex SplitWords();
}
