using System.Security.Cryptography;
using System.Text;

namespace SphereAlert.Services.Scripts
{
    /// <summary>
    /// Loads the finalized sphere-alert.js once at startup and exposes its content,
    /// bytes, and SHA-256 hash. The hash is used for best-effort install detection.
    /// Registered as a singleton.
    /// </summary>
    public class ScriptService
    {
        public const string FileName = "sphere-alert.js";

        public string Content { get; }
        public byte[] Bytes { get; }
        public string Sha256 { get; }

        public ScriptService()
        {
            string path = Path.Combine(AppContext.BaseDirectory, FileName);
            Content = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            Bytes = Encoding.UTF8.GetBytes(Content);
            Sha256 = Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
        }
    }
}
