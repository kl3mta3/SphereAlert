namespace SphereAlert.Models.AlertModels
{
    /// <summary>An immutable audit record of an alert push, clear, or expiry.</summary>
    public class HistoryEntry
    {
        public long Id { get; set; }

        /// <summary>push / clear / expiry</summary>
        public string EventType { get; set; } = string.Empty;

        public string? AlertId { get; set; }
        public string? DomainId { get; set; }
        public string? UserId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string DetailsJson { get; set; } = "{}";

        // Joined display fields.
        public string DomainName { get; set; } = string.Empty;
        public string AlertLevel { get; set; } = string.Empty;
        public string AlertMessage { get; set; } = string.Empty;
    }
}
