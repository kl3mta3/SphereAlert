using SphereAlert.Data.Repositories;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.APISupportedProviders;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.Domains
{
    public class DomainImportResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int Added { get; set; }
        public int TotalZones { get; set; }
    }

    /// <summary>Pulls DNS zones from a provider and adds any not already tracked.</summary>
    public class DomainImportService
    {
        private readonly ProviderRepository _providers;
        private readonly DomainRepository _domains;
        private readonly Logger _logger;

        public DomainImportService(ProviderRepository providers, DomainRepository domains, Logger logger)
        {
            _providers = providers;
            _domains = domains;
            _logger = logger;
        }

        public async Task<DomainImportResult> RefreshAsync(string providerId)
        {
            var provider = await _providers.GetByIdAsync(providerId);
            if (provider == null)
                return new DomainImportResult { Success = false, Error = "Provider not found." };

            try
            {
                var dns = DnsProviderFactory.Create(provider, _logger);
                var zones = await dns.ListZonesAsync();

                int added = 0;
                foreach (var zone in zones)
                {
                    if (string.IsNullOrWhiteSpace(zone.Name))
                        continue;

                    var existing = await _domains.GetByNameAndProviderAsync(zone.Name, providerId);
                    if (existing != null)
                        continue;

                    await _domains.InsertAsync(new DomainRecord
                    {
                        DomainId = Guid.NewGuid().ToString("N"),
                        Name = zone.Name.Trim().ToLowerInvariant(),
                        ProviderId = providerId,
                        LastSyncedAt = DateTime.UtcNow,
                        Status = "unknown",
                        ScriptStatus = "unknown"
                    });
                    added++;
                }

                await _logger.Info($"Domain import for '{provider.DisplayName}': {added} new of {zones.Count} zones.");
                return new DomainImportResult { Success = true, Added = added, TotalZones = zones.Count };
            }
            catch (Exception ex)
            {
                await _logger.Error($"Domain import failed for '{provider.DisplayName}': {ex.Message}");
                return new DomainImportResult { Success = false, Error = ex.Message };
            }
        }
    }
}
