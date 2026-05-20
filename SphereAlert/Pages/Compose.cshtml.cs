using Microsoft.AspNetCore.Mvc;
using SphereAlert.Data.Repositories;
using SphereAlert.Models.AlertModels;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Alerts;

namespace SphereAlert.Pages
{
    public class ComposeModel : AppPageModel
    {
        private readonly DomainRepository _domains;
        private readonly AlertRepository _alerts;
        private readonly AlertService _alertService;

        public ComposeModel(DomainRepository domains, AlertRepository alerts, AlertService alertService)
        {
            _domains = domains;
            _alerts = alerts;
            _alertService = alertService;
        }

        [BindProperty] public string Level { get; set; } = "info";
        [BindProperty] public string Message { get; set; } = string.Empty;
        [BindProperty] public string? EndAtLocal { get; set; }
        [BindProperty] public List<string> SelectedDomainIds { get; set; } = new();
        [BindProperty] public string? EditAlertId { get; set; }

        public List<DomainRecord> AllDomains { get; private set; } = new();
        public bool IsEdit => !string.IsNullOrEmpty(EditAlertId);
        public string? Error { get; private set; }

        public async Task<IActionResult> OnGetAsync(string? alertId)
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            AllDomains = await _domains.GetAllWithDetailAsync();

            if (!string.IsNullOrEmpty(alertId))
            {
                var alert = await _alerts.GetByIdAsync(alertId);
                if (alert == null)
                    return RedirectToPage("/ActiveAlerts");

                EditAlertId = alert.AlertId;
                Level = alert.Level;
                Message = alert.Message;
                EndAtLocal = alert.EndAt?.ToLocalTime().ToString("yyyy-MM-ddTHH:mm");
                SelectedDomainIds = alert.Domains.Select(d => d.DomainId).ToList();
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            AllDomains = await _domains.GetAllWithDetailAsync();

            Message = (Message ?? string.Empty).Trim();

            if (!AlertLevels.IsValidLevel(Level))
            {
                Error = "Choose a valid alert level.";
                return Page();
            }
            if (string.IsNullOrWhiteSpace(Message))
            {
                Error = "Enter an alert message.";
                return Page();
            }
            if (Message.Length > AlertLevels.MaxMessageLength)
            {
                Error = $"Message must be {AlertLevels.MaxMessageLength} characters or fewer.";
                return Page();
            }

            DateTime? endAtUtc = null;
            if (!string.IsNullOrWhiteSpace(EndAtLocal))
            {
                if (!DateTime.TryParse(EndAtLocal, out var parsedLocal))
                {
                    Error = "End time is not a valid date/time.";
                    return Page();
                }
                endAtUtc = DateTime.SpecifyKind(parsedLocal, DateTimeKind.Local).ToUniversalTime();
                if (endAtUtc <= DateTime.UtcNow)
                {
                    Error = "End time must be in the future.";
                    return Page();
                }
            }

            if (IsEdit)
            {
                await _alerts.UpdateAlertContentAsync(EditAlertId!, Level.ToLowerInvariant(), Message, endAtUtc);
                await _alertService.RepushAlertAsync(EditAlertId!, CurrentUser!.UserId, onlyFailed: false);
                TempData["Flash"] = "Alert updated and re-pushed.";
                return RedirectToPage("/Alert", new { alertId = EditAlertId });
            }

            if (SelectedDomainIds.Count == 0)
            {
                Error = "Select at least one domain.";
                return Page();
            }

            var alert = new Alert
            {
                AlertId = Guid.NewGuid().ToString("N"),
                Level = Level.ToLowerInvariant(),
                Message = Message,
                EndAt = endAtUtc,
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = CurrentUser!.UserId
            };

            await _alertService.PushNewAlertAsync(alert, SelectedDomainIds);
            return RedirectToPage("/Alert", new { alertId = alert.AlertId });
        }
    }
}
