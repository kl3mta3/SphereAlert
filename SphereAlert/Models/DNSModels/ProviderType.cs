namespace SphereAlert.Models.DNSModels
{
    /// <summary>
    /// The DNS providers SphereAlert can write TXT records to. Mirrors the provider
    /// set supported by SphereSSL.
    /// </summary>
    public enum ProviderType
    {
        Cloudflare,
        DigitalOcean,
        AWSRoute53,
        Hetzner,
        Namecheap,
        GoDaddy,
        DNSMadeEasy,
        Porkbun,
        Gandi,
        Cloudnsnet,
        DreamHost,
        Vultr,
        Linode,
        DuckDNS
    }

    /// <summary>
    /// Describes the credential shape each provider expects so the UI can render the
    /// right fields and so helpers can document the format they parse.
    /// </summary>
    public static class ProviderCredentialInfo
    {
        /// <summary>Human-readable hint for the credential field on the Add/Edit Provider form.</summary>
        public static string Hint(ProviderType type) => type switch
        {
            ProviderType.Cloudflare   => "API Token with Zone:DNS:Edit permission",
            ProviderType.DigitalOcean => "Personal Access Token",
            ProviderType.AWSRoute53   => "AccessKeyId:SecretAccessKey",
            ProviderType.Hetzner      => "DNS API Token",
            ProviderType.Namecheap    => "ApiUser:ApiKey:ClientIp",
            ProviderType.GoDaddy      => "ApiKey:ApiSecret",
            ProviderType.DNSMadeEasy  => "ApiKey:SecretKey",
            ProviderType.Porkbun      => "ApiKey:SecretApiKey",
            ProviderType.Gandi        => "Personal Access Token (or API key)",
            ProviderType.Cloudnsnet   => "AuthId:AuthPassword",
            ProviderType.DreamHost    => "API Key",
            ProviderType.Vultr        => "API Key",
            ProviderType.Linode       => "Personal Access Token",
            ProviderType.DuckDNS      => "DuckDNS Token",
            _ => "API credentials"
        };
    }
}
