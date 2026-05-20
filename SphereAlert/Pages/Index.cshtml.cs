using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using SphereAlert.Data.Repositories;
using SphereAlert.Models.UserModels;
using SphereAlert.Services.Security;

namespace SphereAlert.Pages
{
    public class IndexModel : AppPageModel
    {
        private readonly UserRepository _userRepository;

        public IndexModel(UserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        [BindProperty]
        public string Username { get; set; } = string.Empty;

        [BindProperty]
        public string Password { get; set; } = string.Empty;

        public string? Error { get; set; }

        /// <summary>True before the default admin/pass123 login has been changed.</summary>
        public bool IsFirstRun { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            // Already signed in — skip the login form.
            var data = HttpContext.Session.GetString(SessionKey);
            if (!string.IsNullOrEmpty(data))
            {
                var existing = JsonConvert.DeserializeObject<UserSession>(data);
                if (existing != null)
                {
                    return existing.MustChangePassword
                        ? RedirectToPage("/ChangePassword")
                        : RedirectToPage("/Dashboard");
                }
            }

            IsFirstRun = await _userRepository.IsFirstRunAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userRepository.GetUserByUsernameAsync(Username.Trim());
            if (user == null || !PasswordService.VerifyPassword(Password, user.PasswordHash))
            {
                Error = "Invalid username or password.";
                return Page();
            }

            var session = new UserSession
            {
                UserId = user.UserId,
                Username = user.Username,
                MustChangePassword = user.MustChangePassword
            };
            HttpContext.Session.SetString(SessionKey, JsonConvert.SerializeObject(session));

            return user.MustChangePassword
                ? RedirectToPage("/ChangePassword")
                : RedirectToPage("/Dashboard");
        }

        public IActionResult OnGetLogout()
        {
            HttpContext.Session.Clear();
            return RedirectToPage("/Index");
        }
    }
}
