namespace SphereAlert.Models.DNSModels
{
    /// <summary>A managed domain (DNS zone). The alert TXT record lives at alert.&lt;Name&gt;.</summary>
    public class DomainRecord
    {
        public string DomainId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ProviderId { get; set; } = string.Empty;
        public DateTime? LastSyncedAt { get; set; }

        /// <summary>Last known DNS write health: ok / error / unknown.</summary>
        public string Status { get; set; } = "unknown";

        public DateTime? ScriptDetectedAt { get; set; }

        /// <summary>Best-effort sphere-alert.js detection: installed / missing / unknown.</summary>
        public string ScriptStatus { get; set; } = "unknown";

        // Joined display fields (not stored on the Domains row).
        public string ProviderDisplayName { get; set; } = string.Empty;
        public string ProviderType { get; set; } = string.Empty;

        // Populated when the caller asks for current-alert state.
        public string? ActiveAlertLevel { get; set; }
    }
}
