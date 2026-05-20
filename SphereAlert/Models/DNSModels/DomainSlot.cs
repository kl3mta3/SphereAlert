namespace SphereAlert.Models.DNSModels
{
    /// <summary>
    /// The current known state of one alert slot on a domain. Updated every time
    /// SphereAlert writes the slot's TXT record, so reopening a domain shows what
    /// is live in each of its three slots.
    /// </summary>
    public class DomainSlot
    {
        public string DomainId { get; set; } = string.Empty;

        /// <summary>1 (alert), 2 (alert2), or 3 (alert3).</summary>
        public int Slot { get; set; }

        /// <summary>The TXT value currently written to this slot — JSON, or a cleared note.</summary>
        public string? CurrentValue { get; set; }

        /// <summary>The alert occupying this slot, or null when cleared/expired.</summary>
        public string? CurrentAlertId { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}
