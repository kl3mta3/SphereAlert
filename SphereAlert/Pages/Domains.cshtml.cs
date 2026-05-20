using Microsoft.AspNetCore.Mvc;
using SphereAlert.Data.Repositories;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Domains;
using SphereAlert.Services.Scripts;

namespace SphereAlert.Pages
{
    public class DomainsModel : AppPageModel
    {
        private readonly DomainRepository _domains;
        private readonly ProviderRepository _providers;
        private readonly DomainImportService _importService;
        private readonly ScriptInstallDetector _detector;

        public DomainsModel(
            DomainRepository domains, ProviderRepository providers,
            DomainImportService importService, ScriptInstallDetector detector)
        {
            _domains = domains;
            _providers = providers;
            _importService = importService;
            _detector = detector;
        }

        public List<DomainRecord> Domains { get; private set; } = new();
        public List<DnsProviderRecord> Providers { get; private set; } = new();
        public DomainRecord? EditTarget { get; private set; }
        public string? Error { get; private set; }

        [BindProperty] public string InputDomainId { get; set; } = string.Empty;
        [BindProperty] public string InputName { get; set; } = string.Empty;
        [BindProperty] public string InputProviderId { get; set; } = string.Empty;

        private async Task LoadAsync()
        {
            Domains = await _domains.GetAllWithDetailAsync();
            Providers = await _providers.GetAllAsync();
        }

        public async Task<IActionResult> OnGetAsync(string? edit)
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            await LoadAsync();
            if (!string.IsNullOrEmpty(edit))
            {
                EditTarget = Domains.FirstOrDefault(d => d.DomainId == edit);
                if (EditTarget != null)
                {
                    InputDomainId = EditTarget.DomainId;
                    InputName = EditTarget.Name;
                    InputProviderId = EditTarget.ProviderId;
                }
            }
            return Page();
        }

        public async Task<IActionResult> OnPostRefreshAsync(string providerId)
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            var result = await _importService.RefreshAsync(providerId);
            if (result.Success)
                TempData["Flash"] = $"Import complete: {result.Added} new domain(s) from {result.TotalZones} zone(s).";
            else
                TempData["FlashError"] = $"Import failed: {result.Error}";

            return RedirectToPage("/Domains");
        }

        public async Task<IActionResult> OnPostAddAsync()
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            if (string.IsNullOrWhiteSpace(InputName) || string.IsNullOrWhiteSpace(InputProviderId))
            {
                await LoadAsync();
                Error = "A domain name and a provider are required.";
                return Page();
            }

            await _domains.InsertAsync(new DomainRecord
            {
                DomainId = Guid.NewGuid().ToString("N"),
                Name = InputName.Trim().ToLowerInvariant(),
                ProviderId = InputProviderId,
                Status = "unknown",
                ScriptStatus = "unknown"
            });

            TempData["Flash"] = $"Domain '{InputName.Trim()}' added.";
            return RedirectToPage("/Domains");
        }

        public async Task<IActionResult> OnPostEditAsync()
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            var existing = await _domains.GetByIdAsync(InputDomainId);
            if (existing == null)
                return RedirectToPage("/Domains");

            if (string.IsNullOrWhiteSpace(InputName) || string.IsNullOrWhiteSpace(InputProviderId))
            {
                await LoadAsync();
                EditTarget = existing;
                Error = "A domain name and a provider are required.";
                return Page();
            }

            existing.Name = InputName.Trim().ToLowerInvariant();
            existing.ProviderId = InputProviderId;
            await _domains.UpdateAsync(existing);

            TempData["Flash"] = "Domain updated.";
            return RedirectToPage("/Domains");
        }

        public async Task<IActionResult> OnPostDeleteAsync(string domainId)
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            await _domains.DeleteAsync(domainId);
            TempData["Flash"] = "Domain removed.";
            return RedirectToPage("/Domains");
        }

        public async Task<IActionResult> OnPostDetectAsync(string domainId)
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            var domain = await _domains.GetByIdAsync(domainId);
            if (domain == null)
                return RedirectToPage("/Domains");

            string status = await _detector.DetectAsync(domain.Name);
            await _domains.UpdateScriptStatusAsync(domainId, status);
            TempData["Flash"] = $"Script check for {domain.Name}: {status}.";
            return RedirectToPage("/Domains");
        }
    }
}
