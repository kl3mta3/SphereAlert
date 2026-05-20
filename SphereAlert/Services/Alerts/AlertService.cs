using System.Text.Json;
using SphereAlert.Data.Repositories;
using SphereAlert.Models.AlertModels;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.APISupportedProviders;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.Alerts
{
    /// <summary>
    /// Orchestrates the alert lifecycle: pushing a new alert, re-pushing failed
    /// domains, clearing an alert, and expiring it. DNS writes fan out in parallel;
    /// database result writes are serialized afterward to avoid SQLite write contention.
    /// </summary>
    public class AlertService
    {
        private readonly ProviderRepository _providers;
        private readonly DomainRepository _domains;
        private readonly AlertRepository _alerts;
        private readonly HistoryRepository _history;
        private readonly Logger _logger;

        public AlertService(
            ProviderRepository providers, DomainRepository domains, AlertRepository alerts,
            HistoryRepository history, Logger logger)
        {
            _providers = providers;
            _domains = domains;
            _alerts = alerts;
            _history = history;
            _logger = logger;
        }

        /// <summary>Persists a new alert, creates its per-domain rows, and pushes the TXT records.</summary>
        public async Task<Alert> PushNewAlertAsync(Alert alert, IEnumerable<string> domainIds)
        {
            await _alerts.InsertAlertAsync(alert);

            var domains = await _domains.GetByIdsAsync(domainIds);
            foreach (var domain in domains)
            {
                await _alerts.InsertAlertDomainAsync(new AlertDomain
                {
                    AlertId = alert.AlertId,
                    DomainId = domain.DomainId,
                    PushStatus = "pending"
                });
            }

            alert.Domains = await _alerts.GetAlertDomainsAsync(alert.AlertId);
            string value = AlertLevels.BuildTxtValue(alert.Level, alert.Message);
            await FanOutAsync(alert, domains, value, "push", alert.CreatedByUserId);
            return alert;
        }

        /// <summary>Re-pushes an existing alert's current content. Used for retry and after an edit.</summary>
        public async Task RepushAlertAsync(string alertId, string? userId, bool onlyFailed)
        {
            var alert = await _alerts.GetByIdAsync(alertId);
            if (alert == null)
                return;

            var targets = onlyFailed
                ? alert.Domains.Where(ad => !ad.PushStatus.Equals("success", StringComparison.OrdinalIgnoreCase))
                : alert.Domains;

            var domains = await _domains.GetByIdsAsync(targets.Select(t => t.DomainId));
            string value = AlertLevels.BuildTxtValue(alert.Level, alert.Message);
            await FanOutAsync(alert, domains, value, "push", userId);
        }

        /// <summary>Pushes ::none:: to every domain and marks the alert cleared.</summary>
        public async Task ClearAlertAsync(string alertId, string? userId)
        {
            var alert = await _alerts.GetByIdAsync(alertId);
            if (alert == null)
                return;

            var domains = await _domains.GetByIdsAsync(alert.Domains.Select(ad => ad.DomainId));
            await FanOutAsync(alert, domains, AlertLevels.NoneValue, "clear", userId);
            await _alerts.SetStatusAsync(alertId, "cleared", DateTime.UtcNow);
        }

        /// <summary>Pushes ::none:: to every domain and marks the alert expired. Used by the scheduler.</summary>
        public async Task ExpireAlertAsync(string alertId)
        {
            var alert = await _alerts.GetByIdAsync(alertId);
            if (alert == null)
                return;

            var domains = await _domains.GetByIdsAsync(alert.Domains.Select(ad => ad.DomainId));
            await FanOutAsync(alert, domains, AlertLevels.NoneValue, "expiry", null);
            await _alerts.SetStatusAsync(alertId, "expired", DateTime.UtcNow);
        }

        private async Task FanOutAsync(
            Alert alert, List<DomainRecord> domains, string value, string eventType, string? userId)
        {
            var domainById = domains.ToDictionary(d => d.DomainId);

            // Resolve and cache each provider's decrypted credentials once.
            var providerCache = new Dictionary<string, DnsProviderRecord?>();
            foreach (var providerId in domains.Select(d => d.ProviderId).Distinct())
                providerCache[providerId] = await _providers.GetByIdAsync(providerId);

            var targets = alert.Domains.Where(ad => domainById.ContainsKey(ad.DomainId)).ToList();

            // Fan out the DNS writes in parallel — no database access inside these tasks.
            var pushTasks = targets.Select(async alertDomain =>
            {
                var domain = domainById[alertDomain.DomainId];
                var providerRecord = providerCache.GetValueOrDefault(domain.ProviderId);
                if (providerRecord == null)
                    return (alertDomain, false, (string?)"DNS provider no longer exists.");

                try
                {
                    var provider = DnsProviderFactory.Create(providerRecord, _logger);
                    var result = await provider.UpsertTxtRecordAsync(
                        domain.Name, AlertLevels.Subdomain, value, AlertLevels.RecordTtlSeconds);
                    return (alertDomain, result.Success, result.Error);
                }
                catch (Exception ex)
                {
                    return (alertDomain, false, (string?)ex.Message);
                }
            }).ToList();

            var results = await Task.WhenAll(pushTasks);

            // Serialize the database writes after the parallel DNS work completes.
            foreach (var (alertDomain, success, error) in results)
            {
                string status = success ? "success" : "failed";
                await _alerts.UpdateAlertDomainResultAsync(alertDomain.Id, status, error);
                await _domains.UpdateSyncStatusAsync(alertDomain.DomainId, success ? "ok" : "error");

                await _history.InsertAsync(new HistoryEntry
                {
                    EventType = eventType,
                    AlertId = alert.AlertId,
                    DomainId = alertDomain.DomainId,
                    UserId = userId,
                    Timestamp = DateTime.UtcNow,
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        level = alert.Level,
                        value,
                        status,
                        error
                    })
                });
            }
        }
    }
}
