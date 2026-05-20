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
        [BindProperty] public bool Dismissable { get; set; } = true;
        [BindProperty] public bool ForceScroll { get; set; }
        [BindProperty] public List<string> SelectedDomainIds { get; set; } = new();

        /// <summary>One "domainId:slot" entry per domain row in the picker.</summary>
        [BindProperty] public List<string> SlotSelections { get; set; } = new();

        [BindProperty] public string? EditAlertId { get; set; }

        public List<DomainRecord> AllDomains { get; private set; } = new();

        /// <summary>In edit mode: the slot each domain is locked to.</summary>
        public Dictionary<string, int> EditSlots { get; private set; } = new();

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
                EndAtLocal = alert.EndAt?.ToLocalTime().ToString("yyyy-MM-ddTHH:mm");
                SelectedDomainIds = alert.Domains.Select(d => d.DomainId).ToList();
                EditSlots = alert.Domains.ToDictionary(d => d.DomainId, d => d.Slot);
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
                await _alerts.UpdateAlertContentAsync(
                    EditAlertId!, Level.ToLowerInvariant(), Message, endAtUtc, Dismissable, ForceScroll);
                await _alertService.RepushAlertAsync(EditAlertId!, CurrentUser!.UserId, onlyFailed: false);
                TempData["Flash"] = "Alert updated and re-pushed.";
                return RedirectToPage("/Alert", new { alertId = EditAlertId });
            }

            if (SelectedDomainIds.Count == 0)
            {
                Error = "Select at least one domain.";
                return Page();
            }

            // Map each selected domain to its chosen slot (default slot 1).
            var slotByDomain = new Dictionary<string, int>();
            foreach (var entry in SlotSelections)
            {
                int sep = entry.LastIndexOf(':');
                if (sep <= 0) continue;
                string domainId = entry[..sep];
                if (int.TryParse(entry[(sep + 1)..], out int slot) && slot >= 1 && slot <= AlertLevels.SlotCount)
                    slotByDomain[domainId] = slot;
            }

            var domainSlots = new Dictionary<string, int>();
            foreach (var domainId in SelectedDomainIds.Distinct())
                domainSlots[domainId] = slotByDomain.GetValueOrDefault(domainId, 1);

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
