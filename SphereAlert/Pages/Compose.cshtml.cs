using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using SphereAlert.Data.Repositories;
using SphereAlert.Models.AlertModels;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Alerts;

namespace SphereAlert.Pages
{
    /// <summary>One slot editor's posted fields. The composer has three of these.</summary>
    public class SlotInput
    {
        public string Level { get; set; } = "info";
        public string Message { get; set; } = string.Empty;
        public bool Dismissable { get; set; } = true;
        public bool ForceScroll { get; set; }

        /// <summary>Expire time as a UTC ISO string — the browser converts the local input.</summary>
        public string? ExpireUtc { get; set; }
    }

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

        /// <summary>Every domain dropdown's value. The first is the "primary" that drives pre-fill.</summary>
        [BindProperty] public List<string> SelectedDomainIds { get; set; } = new();

        /// <summary>Three slot editors — index 0 = slot 1 (alert), 1 = slot 2, 2 = slot 3.</summary>
        [BindProperty] public List<SlotInput> Slots { get; set; } = new();

        public List<DomainRecord> InstalledDomains { get; private set; } = new();
        public string? Error { get; private set; }

        public string? PrimaryDomainId => SelectedDomainIds.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

        public async Task<IActionResult> OnGetAsync(string? domainId)
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            await LoadInstalledDomainsAsync();

            // After a send the page reloads here; restore the domain selection.
            if (TempData["ComposeDomains"] is string restored && restored.Length > 0)
                SelectedDomainIds = restored.Split(',').ToList();
            else if (!string.IsNullOrWhiteSpace(domainId))
                SelectedDomainIds = new List<string> { domainId };

            Slots = new List<SlotInput> { new(), new(), new() };
            if (PrimaryDomainId != null)
                await PrefillSlotsAsync(PrimaryDomainId);

            return Page();
        }

        public async Task<IActionResult> OnPostSendAsync(int slot)
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            await LoadInstalledDomainsAsync();
            EnsureThreeSlots();

            if (slot < 1 || slot > AlertLevels.SlotCount)
            {
                Error = "Unknown alert slot.";
                return Page();
            }

            var input = Slots[slot - 1];
            input.Message = (input.Message ?? string.Empty).Trim();

            if (!AlertLevels.IsValidLevel(input.Level))
            {
                Error = $"Slot {slot}: choose a valid level.";
                return Page();
            }
            if (string.IsNullOrWhiteSpace(input.Message))
            {
                Error = $"Slot {slot}: enter a message.";
                return Page();
            }
            if (input.Message.Length > AlertLevels.MaxMessageLength)
            {
                Error = $"Slot {slot}: message must be {AlertLevels.MaxMessageLength} characters or fewer.";
                return Page();
            }

            DateTime? endAtUtc = null;
            if (!string.IsNullOrWhiteSpace(input.ExpireUtc))
            {
                if (!DateTime.TryParse(input.ExpireUtc, null,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    Error = $"Slot {slot}: expire time is not a valid date/time.";
                    return Page();
                }
                endAtUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                if (endAtUtc <= DateTime.UtcNow)
                {
                    Error = $"Slot {slot}: expire time must be in the future.";
                    return Page();
                }
            }

            var domainIds = SelectedDomainIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();
            if (domainIds.Count == 0)
            {
                Error = "Select at least one domain.";
                return Page();
            }

            var alert = new Alert
            {
                AlertId = Guid.NewGuid().ToString("N"),
                Level = input.Level.ToLowerInvariant(),
                Message = input.Message,
                EndAt = endAtUtc,
                Dismissable = input.Dismissable,
                ForceScroll = input.ForceScroll,
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = CurrentUser!.UserId
            };

            // Push this one slot to every selected domain.
            var domainSlots = domainIds.ToDictionary(id => id, _ => slot);
            await _alertService.PushNewAlertAsync(alert, domainSlots);

            // Keep the domain selection and reload so the editors refresh from live state.
            TempData["ComposeDomains"] = string.Join(",", domainIds);
            TempData["Flash"] = $"Slot {slot} ({AlertLevels.SlotSubdomain(slot)}) sent to {domainIds.Count} domain(s).";
            return RedirectToPage("/Compose");
        }

        private async Task LoadInstalledDomainsAsync()
        {
            var all = await _domains.GetAllWithDetailAsync();
            InstalledDomains = all.Where(d => d.ScriptStatus == "installed").ToList();
        }

        private void EnsureThreeSlots()
        {
            while (Slots.Count < AlertLevels.SlotCount)
                Slots.Add(new SlotInput());
        }

        /// <summary>Loads the primary domain's live slot alerts into the three editors.</summary>
        private async Task PrefillSlotsAsync(string domainId)
        {
            var states = await _domains.GetSlotStatesAsync(domainId);
            for (int slot = 1; slot <= AlertLevels.SlotCount; slot++)
            {
                var state = states.FirstOrDefault(s => s.Slot == slot);
                if (state?.CurrentAlertId == null)
                    continue;

                var alert = await _alerts.GetByIdAsync(state.CurrentAlertId);
                if (alert == null || alert.Status != "active")
                    continue;

                Slots[slot - 1] = new SlotInput
                {
                    Level = alert.Level,
                    Message = alert.Message,
                    Dismissable = alert.Dismissable,
                    ForceScroll = alert.ForceScroll,
                    ExpireUtc = alert.EndAt?.ToString("o")
                };
            }
        }
    }
}
