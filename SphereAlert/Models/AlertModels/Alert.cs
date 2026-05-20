namespace SphereAlert.Models.AlertModels
{
    public class Alert
    {
        public string AlertId { get; set; } = string.Empty;
        public string Level { get; set; } = "info";
        public string Message { get; set; } = string.Empty;

        /// <summary>UTC. Null means the alert persists until manually cleared.</summary>
        public DateTime? EndAt { get; set; }

        /// <summary>Whether visitors can dismiss the banner ("d" in the TXT JSON).</summary>
        public bool Dismissable { get; set; } = true;

        /// <summary>Force scroll-on-hover even when the message fits ("s" in the TXT JSON).</summary>
        public bool ForceScroll { get; set; } = false;

        /// <summary>active / expired / cleared</summary>
        public string Status { get; set; } = "active";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedByUserId { get; set; } = string.Empty;
        public DateTime? ExpiredAt { get; set; }

        // Populated when the caller asks for per-domain push detail.
        public List<AlertDomain> Domains { get; set; } = new();
    }
}
