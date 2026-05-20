using Microsoft.AspNetCore.Mvc;
using SphereAlert.Data.Repositories;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.APISupportedProviders;
using SphereAlert.Services.Config;

namespace SphereAlert.Pages
{
    public class ProvidersModel : AppPageModel
    {
        private readonly ProviderRepository _providers;
        private readonly DomainRepository _domains;
        private readonly Logger _logger;

        public ProvidersModel(ProviderRepository providers, DomainRepository domains, Logger logger)
        {
            _providers = providers;
            _domains = domains;
            _logger = logger;
        }

        public List<DnsProviderRecord> Providers { get; private set; } = new();
        public Dictionary<string, int> DomainCounts { get; private set; } = new();
        public DnsProviderRecord? EditTarget { get; private set; }
        public string? Error { get; private set; }

        [BindProperty] public string InputProviderId { get; set; } = string.Empty;
        [BindProperty] public string InputType { get; set; } = "Cloudflare";
        [BindProperty] public string InputDisplayName { get; set; } = string.Empty;
        [BindProperty] public string InputCredentials { get; set; } = string.Empty;

        private async Task LoadAsync()
        {
            Providers = await _providers.GetAllAsync();
            foreach (var provider in Providers)
                DomainCounts[provider.ProviderId] = await _domains.CountByProviderAsync(provider.ProviderId);
        }

        public async Task<IActionResult> OnGetAsync(string? edit)
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            await LoadAsync();
            if (!string.IsNullOrEmpty(edit))
            {
                EditTarget = Providers.FirstOrDefault(p => p.ProviderId == edit);
                if (EditTarget != null)
                {
                    InputProviderId = EditTarget.ProviderId;
                    InputType = EditTarget.Type;
                    InputDisplayName = EditTarget.DisplayName;
                }
            }
            return Page();
        }

        public async Task<IActionResult> OnPostAddAsync()
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            if (!Enum.TryParse<ProviderType>(InputType, out _))
            {
                await LoadAsync();
                Error = "Choose a valid provider type.";
                return Page();
            }
            if (string.IsNullOrWhiteSpace(InputDisplayName) || string.IsNullOrWhiteSpace(InputCredentials))
            {
                await LoadAsync();
                Error = "A display name and credentials are required.";
                return Page();
            }

            await _providers.InsertAsync(new DnsProviderRecord
            {
                ProviderId = Guid.NewGuid().ToString("N"),
                Type = InputType,
                DisplayName = InputDisplayName.Trim(),
                Credentials = InputCredentials.Trim(),
                CreatedAt = DateTime.UtcNow,
                LastTestResult = string.Empty
            });

            TempData["Flash"] = $"Provider '{InputDisplayName.Trim()}' added.";
            return RedirectToPage("/Providers");
        }

        public async Task<IActionResult> OnPostEditAsync()
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            var existing = await _providers.GetByIdAsync(InputProviderId);
            if (existing == null)
                return RedirectToPage("/Providers");

            if (string.IsNullOrWhiteSpace(InputDisplayName) || !Enum.TryParse<ProviderType>(InputType, out _))
            {
                await LoadAsync();
                EditTarget = existing;
                Error = "A display name and a valid provider type are required.";
                return Page();
            }

            existing.Type = InputType;
            existing.DisplayName = InputDisplayName.Trim();
            // An empty credentials field means "keep the stored credentials".
            if (!string.IsNullOrWhiteSpace(InputCredentials))
                existing.Credentials = InputCredentials.Trim();

            await _providers.UpdateAsync(existing);
            TempData["Flash"] = $"Provider '{existing.DisplayName}' updated.";
            return RedirectToPage("/Providers");
        }

        public async Task<IActionResult> OnPostDeleteAsync(string providerId)
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            await _providers.DeleteAsync(providerId);
            TempData["Flash"] = "Provider deleted, along with the domains that depended on it.";
            return RedirectToPage("/Providers");
        }

        public async Task<IActionResult> OnPostTestAsync(string providerId)
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            var provider = await _providers.GetByIdAsync(providerId);
            if (provider == null)
                return RedirectToPage("/Providers");

            try
            {
                var dns = DnsProviderFactory.Create(provider, _logger);
                var result = await dns.TestConnectionAsync();
                await _providers.UpdateTestResultAsync(providerId, result.Success ? "ok" : "error");
                if (result.Success)
                    TempData["Flash"] = $"'{provider.DisplayName}' connection succeeded.";
                else
                    TempData["FlashError"] = $"'{provider.DisplayName}' test failed: {result.Error}";
            }
            catch (Exception ex)
            {
                await _providers.UpdateTestResultAsync(providerId, "error");
                TempData["FlashError"] = $"'{provider.DisplayName}' test failed: {ex.Message}";
            }

            return RedirectToPage("/Providers");
        }
    }
}
