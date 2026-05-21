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

        /// <summary>The slot every selected domain is pushed to: 1 (alert), 2 (alert2), 3 (alert3).</summary>
        [BindProperty] public int Slot { get; set; } = 1;

        [BindProperty] public string Message { get; set; } = string.Empty;

        /// <summary>End time as a UTC ISO-8601 string. The browser converts the
        /// operator's local datetime-local input to UTC before posting.</summary>
        [BindProperty] public string? EndAtUtc { get; set; }

        [BindProperty] public bool Dismissable { get; set; } = true;
        [BindProperty] public bool ForceScroll { get; set; }

        /// <summary>One entry per domain dropdown in the picker. Blank entries are ignored.</summary>
        [BindProperty] public List<string> SelectedDomainIds { get; set; } = new();

        [BindProperty] public string? EditAlertId { get; set; }

        public List<DomainRecord> AllDomains { get; private set; } = new();

        /// <summary>Domains whose script install was verified — the only ones offered for a new alert.</summary>
        public IEnumerable<DomainRecord> InstalledDomains
            => AllDomains.Where(d => d.ScriptStatus == "installed");

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
                Dismissable = alert.Dismissable;
                ForceScroll = alert.ForceScroll;
                EndAtUtc = alert.EndAt?.ToString("o");
                SelectedDomainIds = alert.Domains.Select(d => d.DomainId).ToList();
                Slot = alert.Domains.FirstOrDefault()?.Slot ?? 1;
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
            if (Slot < 1 || Slot > AlertLevels.SlotCount)
            {
                Error = "Choose a valid alert slot.";
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

            // The browser posts EndAtUtc already converted to UTC (it knows the
            // operator's real timezone; the server does not).
            DateTime? endAtUtc = null;
            if (!string.IsNullOrWhiteSpace(EndAtUtc))
            {
                if (!DateTime.TryParse(EndAtUtc, null,
                        System.Globalization.DateTimeStyles.AdjustToUniversal
                        | System.Globalization.DateTimeStyles.AssumeUniversal,
                        out var parsedUtc))
                {
                    Error = "End time is not a valid date/time.";
                    return Page();
                }
                endAtUtc = DateTime.SpecifyKind(parsedUtc, DateTimeKind.Utc);
                if (endAtUtc <= DateTime.UtcNow)
                {
                    Error = "End time must be in the future.";
                    return Page();
                }
            }

            if (IsEdit)
            {
                await _alerts.UpdateAlertContentAsync(
                    EditAlertId!, Level.ToLowerInvariant(), Message, endAtUtc, Dismissable, ForceScroll);
                await _alertService.RepushAlertAsync(EditAlertId!, CurrentUser!.UserId, onlyFailed: false);
                TempData["Flash"] = "Alert updated and re-pushed.";
                return RedirectToPage("/Alert", new { alertId = EditAlertId });
            }

            // Each domain dropdown posts into SelectedDomainIds; drop blanks and dupes.
            var domainIds = SelectedDomainIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            if (domainIds.Count == 0)
            {
                Error = "Select at least one domain.";
                return Page();
            }

            // One slot for the whole push — every selected domain gets it.
            var domainSlots = domainIds.ToDictionary(id => id, _ => Slot);

            var alert = new Alert
            {
                AlertId = Guid.NewGuid().ToString("N"),
                Level = Level.ToLowerInvariant(),
                Message = Message,
                EndAt = endAtUtc,
                Dismissable = Dismissable,
                ForceScroll = ForceScroll,
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = CurrentUser!.UserId
            };

            await _alertService.PushNewAlertAsync(alert, domainSlots);
            return RedirectToPage("/Alert", new { alertId = alert.AlertId });
        }
    }
}
