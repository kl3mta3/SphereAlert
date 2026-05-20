using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.APISupportedProviders
{
    /// <summary>
    /// Resolves a stored provider record (type + decrypted credentials) into a
    /// concrete <see cref="IAlertDnsProvider"/> implementation.
    /// </summary>
    public static class DnsProviderFactory
    {
        public static IAlertDnsProvider Create(DnsProviderRecord record, Logger logger)
            => Create(record.Type, record.Credentials, logger);

        public static IAlertDnsProvider Create(string typeName, string credentials, Logger logger)
        {
            if (!Enum.TryParse<ProviderType>(typeName, ignoreCase: true, out var type))
                throw new NotSupportedException($"Unknown DNS provider type: {typeName}");
            return Create(type, credentials, logger);
        }

        public static IAlertDnsProvider Create(ProviderType type, string credentials, Logger logger) => type switch
        {
            ProviderType.Cloudflare   => new CloudflareProvider(credentials, logger),
            ProviderType.DigitalOcean => new DigitalOceanProvider(credentials, logger),
            ProviderType.AWSRoute53   => new AWSRoute53Provider(credentials, logger),
            ProviderType.Hetzner      => new HetznerProvider(credentials, logger),
            ProviderType.Namecheap    => new NamecheapProvider(credentials, logger),
            ProviderType.GoDaddy      => new GoDaddyProvider(credentials, logger),
            ProviderType.DNSMadeEasy  => new DNSMadeEasyProvider(credentials, logger),
            ProviderType.Porkbun      => new PorkbunProvider(credentials, logger),
            ProviderType.Gandi        => new GandiProvider(credentials, logger),
            ProviderType.Cloudnsnet   => new CloudnsnetProvider(credentials, logger),
            ProviderType.DreamHost    => new DreamHostProvider(credentials, logger),
            ProviderType.Vultr        => new VultrProvider(credentials, logger),
            ProviderType.Linode       => new LinodeProvider(credentials, logger),
            ProviderType.DuckDNS      => new DuckDNSProvider(credentials, logger),
            _ => throw new NotSupportedException($"Unsupported DNS provider type: {type}")
        };
    }
}
