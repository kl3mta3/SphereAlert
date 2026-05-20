using Microsoft.AspNetCore.Mvc;
using SphereAlert.Data.Repositories;
using SphereAlert.Models.AlertModels;

namespace SphereAlert.Pages
{
    public class DashboardModel : AppPageModel
    {
        private readonly ProviderRepository _providers;
        private readonly DomainRepository _domains;
        private readonly AlertRepository _alerts;
        private readonly HistoryRepository _history;

        public DashboardModel(
            ProviderRepository providers, DomainRepository domains,
            AlertRepository alerts, HistoryRepository history)
        {
            _providers = providers;
            _domains = domains;
            _alerts = alerts;
            _history = history;
        }

        public int ProviderCount { get; private set; }
        public int DomainCount { get; private set; }
        public int ActiveAlertCount { get; private set; }
        public List<Alert> ActiveAlerts { get; private set; } = new();
        public List<HistoryEntry> RecentHistory { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            ProviderCount = (await _providers.GetAllAsync()).Count;
            DomainCount = await _domains.CountAsync();
            ActiveAlerts = await _alerts.GetActiveAsync();
            ActiveAlertCount = ActiveAlerts.Count;
            RecentHistory = (await _history.GetAsync()).Take(10).ToList();

            return Page();
        }
    }
}
