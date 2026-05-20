namespace SphereAlert.Models.AlertModels
{
    /// <summary>One row per (alert, domain) pair — the fan-out target and its push result.</summary>
    public class AlertDomain
    {
        public long Id { get; set; }
        public string AlertId { get; set; } = string.Empty;
        public string DomainId { get; set; } = string.Empty;

        /// <summary>pending / success / failed</summary>
        public string PushStatus { get; set; } = "pending";
        public string? PushError { get; set; }
        public DateTime? PushedAt { get; set; }

        // Joined display field.
        public string DomainName { get; set; } = string.Empty;
    }
}
