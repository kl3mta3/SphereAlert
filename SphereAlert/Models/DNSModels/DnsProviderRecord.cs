namespace SphereAlert.Models.DNSModels
{
    /// <summary>A stored DNS provider credential set. <see cref="Credentials"/> is the
    /// decrypted plaintext and is never persisted directly — the repository encrypts it.</summary>
    public class DnsProviderRecord
    {
        public string ProviderId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Decrypted credential string. Populated on read, encrypted on write.</summary>
        public string Credentials { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastTestedAt { get; set; }
        public string LastTestResult { get; set; } = string.Empty;
    }
}
