using Microsoft.AspNetCore.Mvc;
using SphereAlert.Data.Repositories;
using SphereAlert.Models.AlertModels;
using SphereAlert.Services.Alerts;

namespace SphereAlert.Pages
{
    public class ActiveAlertsModel : AppPageModel
    {
        private readonly AlertRepository _alerts;
        private readonly AlertService _alertService;

        public ActiveAlertsModel(AlertRepository alerts, AlertService alertService)
        {
            _alerts = alerts;
            _alertService = alertService;
        }

        public List<Alert> Alerts { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            Alerts = await _alerts.GetActiveAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostClearAsync(string alertId)
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            await _alertService.ClearAlertAsync(alertId, CurrentUser!.UserId);
            TempData["Flash"] = "Alert cleared — the banner has been removed from its domains.";
            return RedirectToPage("/ActiveAlerts");
        }
    }
}
