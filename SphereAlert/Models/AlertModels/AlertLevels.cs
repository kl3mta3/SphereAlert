using System.Text.Json;

namespace SphereAlert.Models.AlertModels
{
    /// <summary>
    /// Alert level vocabulary and TXT-record value formatting. The value format is
    /// fixed by sphere-alert.js: a JSON object {"l","m","d","s"}. The script reads
    /// three slots per domain — alert / alert2 / alert3.
    /// </summary>
    public static class AlertLevels
    {
        /// <summary>Level names, indexed 0-4 — the index is the "l" value in the JSON.</summary>
        public static readonly string[] All = { "info", "low", "medium", "high", "critical" };

        /// <summary>Composer cap — matches the script's 240-char message truncation.</summary>
        public const int MaxMessageLength = 240;

        /// <summary>Number of alert slots per domain.</summary>
        public const int SlotCount = 3;

        /// <summary>TTL used for alert TXT records — short so alerts propagate quickly.</summary>
        public const int RecordTtlSeconds = 60;

        public static bool IsValidLevel(string? level)
            => !string.IsNullOrWhiteSpace(level) && All.Contains(level.ToLowerInvariant());

        /// <summary>Maps a level name to its 0-4 index ("l" in the JSON). Unknown → 0 (info).</summary>
        public static int LevelIndex(string? level)
        {
            int idx = Array.IndexOf(All, (level ?? string.Empty).ToLowerInvariant());
            return idx < 0 ? 0 : idx;
        }

        /// <summary>The subdomain a slot writes to: slot 1 → "alert", 2 → "alert2", 3 → "alert3".</summary>
        public static string SlotSubdomain(int slot)
            => slot <= 1 ? "alert" : $"alert{slot}";

        /// <summary>Human label for a slot, e.g. "Slot 2 (alert2)".</summary>
        public static string SlotLabel(int slot) => $"Slot {slot} ({SlotSubdomain(slot)})";

        /// <summary>
        /// Builds the JSON TXT value an alert push writes — e.g.
        /// {"l":2,"m":"Maintenance Sat 7am-9am","d":1,"s":0}
        /// </summary>
        public static string BuildJsonValue(string level, string message, bool dismissable, bool forceScroll)
        {
            var payload = new
            {
                l = LevelIndex(level),
                m = message.Trim(),
                d = dismissable ? 1 : 0,
                s = forceScroll ? 1 : 0
            };
            return JsonSerializer.Serialize(payload);
        }

        /// <summary>
        /// The non-JSON value written when an alert is cleared or expires. The script
        /// renders no banner for it, and the record itself becomes a human-readable note.
        /// </summary>
        public static string BuildClearedNote(string priorMessage)
            => $"Cleared {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC — previous: {priorMessage}";
    }
}
