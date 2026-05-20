using SphereAlert.Services.Security;

namespace SphereAlert.Services.Config
{
    /// <summary>
    /// Central runtime configuration. SphereAlert is self-hosted and operator-owned,
    /// so configuration is intentionally minimal: a couple of environment variables
    /// and sensible defaults. Everything else lives in the database.
    /// </summary>
    public class ConfigureService
    {
        public const string AppName = "SphereAlert";

        // The container always listens on 7227 internally; operators map it externally.
        public const int ServerPort = 7227;
        public const string ServerIP = "0.0.0.0";

        // First-run seed credentials. The admin is forced to change the password on first login.
        public const string DefaultAdminUsername = "admin";
        public const string DefaultAdminPassword = "pass123";

        public static string DataDir { get; private set; } = "data";
        public static string DbPath => Path.Combine(DataDir, "spherealert.db");
        public static string KeyFilePath => Path.Combine(DataDir, ".keyfile");
        public static string LogFilePath => Path.Combine(DataDir, "spherealert.log");

        public static string LogLevel { get; private set; } = "Info";

        // Seed values used by DatabaseManager when no users exist yet.
        public static string SeedUsername { get; private set; } = DefaultAdminUsername;
        public static string SeedPasswordHash { get; private set; } = string.Empty;

        public static bool IsSetup { get; set; } = false;

        public static void Load()
        {
            var dataDir = Environment.GetEnvironmentVariable("SPHEREALERT_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(dataDir))
                DataDir = dataDir.Trim();

            var logLevel = Environment.GetEnvironmentVariable("SPHEREALERT_LOG_LEVEL");
            if (!string.IsNullOrWhiteSpace(logLevel))
                LogLevel = logLevel.Trim();

            Directory.CreateDirectory(DataDir);

            SeedUsername = DefaultAdminUsername;
            SeedPasswordHash = PasswordService.HashPassword(DefaultAdminPassword);
        }
    }
}
