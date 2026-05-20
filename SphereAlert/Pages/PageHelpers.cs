namespace SphereAlert.Pages
{
    /// <summary>Small presentation helpers shared across Razor pages.</summary>
    public static class PageHelpers
    {
        /// <summary>CSS class for an alert-level badge (mirrors sphere-alert.js levels).</summary>
        public static string LevelClass(string? level)
            => "lvl-" + (string.IsNullOrWhiteSpace(level) ? "none" : level.ToLowerInvariant());

        /// <summary>The display label sphere-alert.js renders for each level.</summary>
        public static string LevelLabel(string? level) => (level ?? string.Empty).ToLowerInvariant() switch
        {
            "info" => "Info",
            "low" => "Notice",
            "medium" => "Warning",
            "high" => "Alert",
            "critical" => "Critical",
            "none" => "None",
            _ => level ?? string.Empty
        };

        /// <summary>CSS class for a status badge (domain status, push status, script status).</summary>
        public static string StatusBadge(string? status) => (status ?? string.Empty).ToLowerInvariant() switch
        {
            "ok" or "success" => "badge-ok",
            "error" or "failed" => "badge-error",
            "installed" => "badge-installed",
            "missing" => "badge-missing",
            _ => "badge-unknown"
        };

        /// <summary>Formats a UTC timestamp for display.</summary>
        public static string FormatTime(DateTime? utc)
            => utc.HasValue ? utc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";
    }
}
