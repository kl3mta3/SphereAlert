namespace SphereAlert.Models.UserModels
{
    /// <summary>Serialized into HttpContext.Session under the key "UserSession".</summary>
    public class UserSession
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public bool MustChangePassword { get; set; }
    }
}
