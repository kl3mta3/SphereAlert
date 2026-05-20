using Microsoft.AspNetCore.Mvc;
using SphereAlert.Data.Repositories;
using SphereAlert.Models.AlertModels;
using SphereAlert.Models.DNSModels;

namespace SphereAlert.Pages
{
    public class DomainModel : AppPageModel
    {
        private readonly DomainRepository _domains;
        private readonly AlertRepository _alerts;

        public DomainModel(DomainRepository domains, AlertRepository alerts)
        {
            _domains = domains;
            _alerts = alerts;
        }

        public class SlotView
        {
            public int Slot { get; set; }
            public string RecordName { get; set; } = string.Empty;
            public DomainSlot? State { get; set; }
            public Alert? Alert { get; set; }
        }

        public DomainRecord? Domain { get; private set; }
        public List<SlotView> Slots { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync(string domainId)
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            Domain = await _domains.GetByIdAsync(domainId);
            if (Domain == null)
                return RedirectToPage("/Domains");

            var states = await _domains.GetSlotStatesAsync(domainId);

            for (int slot = 1; slot <= AlertLevels.SlotCount; slot++)
            {
                var state = states.FirstOrDefault(s => s.Slot == slot);
                Alert? alert = null;
                if (state?.CurrentAlertId != null)
                    alert = await _alerts.GetByIdAsync(state.CurrentAlertId);

                Slots.Add(new SlotView
                {
                    Slot = slot,
                    RecordName = $"{AlertLevels.SlotSubdomain(slot)}.{Domain.Name}",
                    State = state,
                    Alert = alert
                });
            }

            return Page();
        }
    }
}
