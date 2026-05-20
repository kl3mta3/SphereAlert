using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Newtonsoft.Json;
using SphereAlert.Models.UserModels;

namespace SphereAlert.Pages
{
    /// <summary>
    /// Base for every authenticated page. Handles the session check and the
    /// forced-password-change redirect so individual pages stay focused.
    /// </summary>
    public abstract class AppPageModel : PageModel
    {
        public const string SessionKey = "UserSession";

        public UserSession? CurrentUser { get; private set; }

        /// <summary>
        /// Returns a redirect when the request is not allowed to proceed (no session,
        /// or a pending forced password change), or null when the page may render.
        /// </summary>
        protected IActionResult? RequireAuth(bool allowPasswordChangePending = false)
        {
            var data = HttpContext.Session.GetString(SessionKey);
            if (string.IsNullOrEmpty(data))
                return RedirectToPage("/Index");

            CurrentUser = JsonConvert.DeserializeObject<UserSession>(data);
            if (CurrentUser == null)
                return RedirectToPage("/Index");

            if (CurrentUser.MustChangePassword && !allowPasswordChangePending)
                return RedirectToPage("/ChangePassword");

            return null;
        }
    }
}
