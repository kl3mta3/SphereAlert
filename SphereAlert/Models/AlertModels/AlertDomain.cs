namespace SphereAlert.Models.AlertModels
{
    /// <summary>One row per (alert, domain) pair — the fan-out target and its push result.</summary>
    public class AlertDomain
    {
        public long Id { get; set; }
        public string AlertId { get; set; } = string.Empty;
        public string DomainId { get; set; } = string.Empty;

        /// <summary>Which alert slot on the domain this targets: 1 (alert), 2 (alert2), 3 (alert3).</summary>
        public int Slot { get; set; } = 1;

        /// <summary>The exact TXT value written for this (alert, domain) — what we sent.</summary>
        public string? PushedValue { get; set; }

        /// <summary>pending / success / failed</summary>
        public string PushStatus { get; set; } = "pending";
        public string? PushError { get; set; }
        public DateTime? PushedAt { get; set; }

        // Joined display field.
        public string DomainName { get; set; } = string.Empty;
    }
}
