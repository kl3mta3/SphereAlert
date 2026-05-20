using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using SphereAlert.Data.Repositories;
using SphereAlert.Models.UserModels;
using SphereAlert.Services.Security;

namespace SphereAlert.Pages
{
    public class ChangePasswordModel : AppPageModel
    {
        private readonly UserRepository _userRepository;

        public ChangePasswordModel(UserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        [BindProperty] public string NewUsername { get; set; } = string.Empty;
        [BindProperty] public string CurrentPassword { get; set; } = string.Empty;
        [BindProperty] public string NewPassword { get; set; } = string.Empty;
        [BindProperty] public string ConfirmPassword { get; set; } = string.Empty;

        public string? Error { get; set; }
        public bool Forced { get; private set; }

        public IActionResult OnGet()
        {
            var redirect = RequireAuth(allowPasswordChangePending: true);
            if (redirect != null) return redirect;

            Forced = CurrentUser!.MustChangePassword;
            NewUsername = CurrentUser.Username;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var redirect = RequireAuth(allowPasswordChangePending: true);
            if (redirect != null) return redirect;

            Forced = CurrentUser!.MustChangePassword;

            var user = await _userRepository.GetUserByIdAsync(CurrentUser.UserId);
            if (user == null)
                return RedirectToPage("/Index");

            if (!PasswordService.VerifyPassword(CurrentPassword, user.PasswordHash))
            {
                Error = "Current password is incorrect.";
                return Page();
            }

            NewUsername = (NewUsername ?? string.Empty).Trim();
            string? usernameError = await ValidateUsernameAsync(NewUsername, user.UserId);
            if (usernameError != null)
            {
                Error = usernameError;
                return Page();
            }

            string? strengthError = ValidateStrength(NewPassword);
            if (strengthError != null)
            {
                Error = strengthError;
                return Page();
            }

            if (NewPassword != ConfirmPassword)
            {
                Error = "New password and confirmation do not match.";
                return Page();
            }

            if (PasswordService.VerifyPassword(NewPassword, user.PasswordHash))
            {
                Error = "The new password must be different from the current one.";
                return Page();
            }

            await _userRepository.UpdateAccountAsync(
                user.UserId, NewUsername, PasswordService.HashPassword(NewPassword));

            // Refresh the session: the forced-change gate lifts and the username may have changed.
            var session = new UserSession
            {
                UserId = user.UserId,
                Username = NewUsername,
                MustChangePassword = false
            };
            HttpContext.Session.SetString(SessionKey, JsonConvert.SerializeObject(session));

            TempData["Flash"] = "Account updated.";
            return RedirectToPage("/Dashboard");
        }

        private async Task<string?> ValidateUsernameAsync(string username, string currentUserId)
        {
            if (username.Length < 3 || username.Length > 32)
                return "Username must be 3–32 characters long.";
            if (!Regex.IsMatch(username, "^[A-Za-z0-9._-]+$"))
                return "Username may only contain letters, numbers, and . _ - characters.";

            var existing = await _userRepository.GetUserByUsernameAsync(username);
            if (existing != null && existing.UserId != currentUserId)
                return "That username is already taken.";

            return null;
        }

        private static string? ValidateStrength(string password)
        {
            if (password.Length < 8 || password.Length > 64)
                return "Password must be 8–64 characters long.";
            if (!password.Any(char.IsUpper))
                return "Password must include an uppercase letter.";
            if (!password.Any(char.IsLower))
                return "Password must include a lowercase letter.";
            if (!password.Any(char.IsDigit))
                return "Password must include a number.";
            return null;
        }
    }
}
