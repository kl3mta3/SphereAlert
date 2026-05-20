using Microsoft.AspNetCore.Mvc;

namespace SphereAlert.Pages
{
    public class SettingsModel : AppPageModel
    {
        public IActionResult OnGet()
        {
            var redirect = RequireAuth();
            return redirect ?? Page();
        }
    }
}
