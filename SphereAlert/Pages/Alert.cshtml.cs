using Microsoft.AspNetCore.Mvc;
using SphereAlert.Data.Repositories;
using SphereAlert.Services.Alerts;

namespace SphereAlert.Pages
{
    public class AlertModel : AppPageModel
    {
        private readonly AlertRepository _alerts;
        private readonly AlertService _alertService;

        public AlertModel(AlertRepository alerts, AlertService alertService)
        {
            _alerts = alerts;
            _alertService = alertService;
        }

        public Models.AlertModels.Alert? Alert { get; private set; }

        public int SuccessCount => Alert?.Domains.Count(d => d.PushStatus == "success") ?? 0;
        public int FailedCount => Alert?.Domains.Count(d => d.PushStatus == "failed") ?? 0;

        public async Task<IActionResult> OnGetAsync(string alertId)
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            Alert = await _alerts.GetByIdAsync(alertId);
            if (Alert == null)
                return RedirectToPage("/ActiveAlerts");

            return Page();
        }

        public async Task<IActionResult> OnPostRetryAsync(string alertId)
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            await _alertService.RepushAlertAsync(alertId, CurrentUser!.UserId, onlyFailed: true);
            TempData["Flash"] = "Retried the failed domains.";
            return RedirectToPage("/Alert", new { alertId });
        }

        public async Task<IActionResult> OnPostClearAsync(string alertId)
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            await _alertService.ClearAlertAsync(alertId, CurrentUser!.UserId);
            TempData["Flash"] = "Alert cleared.";
            return RedirectToPage("/ActiveAlerts");
        }
    }
}
