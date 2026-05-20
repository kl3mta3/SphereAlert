using Microsoft.AspNetCore.Mvc;
using SphereAlert.Data.Repositories;
using SphereAlert.Models.AlertModels;
using SphereAlert.Models.DNSModels;

namespace SphereAlert.Pages
{
    public class HistoryModel : AppPageModel
    {
        private readonly HistoryRepository _history;
        private readonly DomainRepository _domains;

        public HistoryModel(HistoryRepository history, DomainRepository domains)
        {
            _history = history;
            _domains = domains;
        }

        [BindProperty(SupportsGet = true)] public string? FilterDomainId { get; set; }
        [BindProperty(SupportsGet = true)] public string? FilterEventType { get; set; }
        [BindProperty(SupportsGet = true)] public string? FilterLevel { get; set; }
        [BindProperty(SupportsGet = true)] public string? FilterFrom { get; set; }
        [BindProperty(SupportsGet = true)] public string? FilterTo { get; set; }

        public List<DomainRecord> AllDomains { get; private set; } = new();
        public List<HistoryEntry> Entries { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            AllDomains = await _domains.GetAllWithDetailAsync();

            DateTime? from = DateTime.TryParse(FilterFrom, out var f)
                ? DateTime.SpecifyKind(f, DateTimeKind.Local).ToUniversalTime()
                : null;
            DateTime? to = DateTime.TryParse(FilterTo, out var t)
                ? DateTime.SpecifyKind(t.AddDays(1).AddSeconds(-1), DateTimeKind.Local).ToUniversalTime()
                : null;

            Entries = await _history.GetAsync(
                domainId: string.IsNullOrWhiteSpace(FilterDomainId) ? null : FilterDomainId,
                eventType: string.IsNullOrWhiteSpace(FilterEventType) ? null : FilterEventType,
                level: string.IsNullOrWhiteSpace(FilterLevel) ? null : FilterLevel,
                from: from,
                to: to);

            return Page();
        }
    }
}
