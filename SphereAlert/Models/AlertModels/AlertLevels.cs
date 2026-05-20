namespace SphereAlert.Models.AlertModels
{
    /// <summary>
    /// Alert level vocabulary and TXT-record value formatting. The value format is
    /// fixed by sphere-alert.js: "::level:: message", or "::none::" to clear.
    /// </summary>
    public static class AlertLevels
    {
        public static readonly string[] All = { "info", "low", "medium", "high", "critical" };

        /// <summary>The TXT value that hides the banner.</summary>
        public const string NoneValue = "::none::";

        /// <summary>The subdomain the alert TXT record lives at: alert.&lt;domain&gt;.</summary>
        public const string Subdomain = "alert";

        /// <summary>TTL used for alert TXT records — short so alerts propagate quickly.</summary>
        public const int RecordTtlSeconds = 60;

        /// <summary>Composer cap — leaves headroom under the 280-char client limit.</summary>
        public const int MaxMessageLength = 240;

        public static bool IsValidLevel(string? level)
            => !string.IsNullOrWhiteSpace(level) && All.Contains(level.ToLowerInvariant());

        /// <summary>Builds the TXT record value an alert push writes to alert.&lt;domain&gt;.</summary>
        public static string BuildTxtValue(string level, string message)
            => $"::{level.ToLowerInvariant()}:: {message.Trim()}";
    }
}
