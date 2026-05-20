using Microsoft.AspNetCore.Mvc;
using SphereAlert.Services.Scripts;

namespace SphereAlert.Pages
{
    public class InstallModel : AppPageModel
    {
        private readonly ZipInjectionService _zipService;

        public InstallModel(ZipInjectionService zipService)
        {
            _zipService = zipService;
        }

        public string? Error { get; private set; }

        public IActionResult OnGet()
        {
            var redirect = RequireAuth();
            return redirect ?? Page();
        }

        public async Task<IActionResult> OnPostAsync(IFormFile? zipFile)
        {
            var redirect = RequireAuth();
            if (redirect != null) return redirect;

            if (zipFile == null || zipFile.Length == 0)
            {
                Error = "Choose a .zip file to process.";
                return Page();
            }

            try
            {
                using var stream = zipFile.OpenReadStream();
                var result = await _zipService.ProcessAsync(stream);
                return File(result.ZipBytes, "application/zip", "site-with-sphere-alert.zip");
            }
            catch (Exception ex)
            {
                Error = $"Could not process the archive — make sure it is a valid .zip. ({ex.Message})";
                return Page();
            }
        }
    }
}
