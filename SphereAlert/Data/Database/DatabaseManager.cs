using Microsoft.Data.Sqlite;
using SphereAlert.Services.Config;

namespace SphereAlert.Data.Database
{
    /// <summary>
    /// Owns SQLite initialization: schema creation, the default-admin seed, and the
    /// versioned startup migration step. Schema is created with CREATE TABLE IF NOT EXISTS;
    /// future schema changes are conditional ALTER TABLEs gated on the DbVersion row.
    /// </summary>
    public class DatabaseManager
    {
        public const int CurrentSchemaVersion = 2;

        public static string ConnectionString => $"Data Source={ConfigureService.DbPath}";

        public static SqliteConnection CreateConnection() => new(ConnectionString);

        public static async Task<SqliteConnection> OpenConnectionAsync()
        {
            var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            using var pragma = connection.CreateCommand();
            // busy_timeout lets parallel alert fan-out tolerate brief write contention.
            pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            await pragma.ExecuteNonQueryAsync();
            return connection;
        }

        public static async Task Initialize()
        {
            var dbFolder = Path.GetDirectoryName(Path.GetFullPath(ConfigureService.DbPath));
            if (!string.IsNullOrEmpty(dbFolder) && !Directory.Exists(dbFolder))
                Directory.CreateDirectory(dbFolder);

            using var connection = await OpenConnectionAsync();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Users (
                    UserId TEXT PRIMARY KEY,
                    Username TEXT UNIQUE NOT NULL,
                    PasswordHash TEXT NOT NULL,
                    MustChangePassword INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Providers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProviderId TEXT UNIQUE NOT NULL,
                    Type TEXT NOT NULL,
                    DisplayName TEXT NOT NULL,
                    EncryptedCredentials TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    LastTestedAt TEXT,
                    LastTestResult TEXT
                );

                CREATE TABLE IF NOT EXISTS Domains (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DomainId TEXT UNIQUE NOT NULL,
                    Name TEXT NOT NULL,
                    ProviderId TEXT NOT NULL,
                    LastSyncedAt TEXT,
                    Status TEXT NOT NULL DEFAULT 'unknown',
                    ScriptDetectedAt TEXT,
                    ScriptStatus TEXT NOT NULL DEFAULT 'unknown',
                    FOREIGN KEY(ProviderId) REFERENCES Providers(ProviderId) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS Alerts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    AlertId TEXT UNIQUE NOT NULL,
                    Level TEXT NOT NULL,
                    Message TEXT NOT NULL,
                    EndAt TEXT,
                    Status TEXT NOT NULL DEFAULT 'active' CHECK(Status IN ('active','expired','cleared')),
                    CreatedAt TEXT NOT NULL,
                    CreatedByUserId TEXT,
                    ExpiredAt TEXT,
                    Dismissable INTEGER NOT NULL DEFAULT 1,
                    ForceScroll INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS AlertDomains (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    AlertId TEXT NOT NULL,
                    DomainId TEXT NOT NULL,
                    Slot INTEGER NOT NULL DEFAULT 1,
                    PushStatus TEXT NOT NULL DEFAULT 'pending',
                    PushError TEXT,
                    PushedValue TEXT,
                    PushedAt TEXT,
                    FOREIGN KEY(AlertId) REFERENCES Alerts(AlertId) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS DomainSlots (
                    DomainId TEXT NOT NULL,
                    Slot INTEGER NOT NULL,
                    CurrentValue TEXT,
                    CurrentAlertId TEXT,
                    UpdatedAt TEXT,
                    PRIMARY KEY (DomainId, Slot)
                );

                CREATE TABLE IF NOT EXISTS History (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    EventType TEXT NOT NULL,
                    AlertId TEXT,
                    DomainId TEXT,
                    UserId TEXT,
                    Timestamp TEXT NOT NULL,
                    DetailsJson TEXT
                );

                CREATE TABLE IF NOT EXISTS DbVersion (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    Version INTEGER NOT NULL
                );
                ";
                await command.ExecuteNonQueryAsync();
            }

            using (var versionCmd = connection.CreateCommand())
            {
                versionCmd.CommandText = "INSERT OR IGNORE INTO DbVersion(Id, Version) VALUES(1, @v);";
                versionCmd.Parameters.AddWithValue("@v", CurrentSchemaVersion);
                await versionCmd.ExecuteNonQueryAsync();
            }

            await SeedDefaultAdminAsync(connection);
            await MigrateAsync();
        }

        /// <summary>Creates the default admin account when the database has no users.</summary>
        private static async Task SeedDefaultAdminAsync(SqliteConnection connection)
        {
            long userCount;
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(1) FROM Users;";
                userCount = (long)(await countCmd.ExecuteScalarAsync() ?? 0L);
            }

            if (userCount > 0)
                return;

            using var insert = connection.CreateCommand();
            insert.CommandText = @"
                INSERT INTO Users (UserId, Username, PasswordHash, MustChangePassword, CreatedAt)
                VALUES (@userId, @username, @passwordHash, 1, @createdAt);";
            insert.Parameters.AddWithValue("@userId", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("@username", ConfigureService.SeedUsername);
            insert.Parameters.AddWithValue("@passwordHash", ConfigureService.SeedPasswordHash);
            insert.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o"));
            await insert.ExecuteNonQueryAsync();
        }

        public static async Task<int> GetDatabaseVersion()
        {
            using var connection = await OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Version FROM DbVersion WHERE Id = 1;";
            var result = await command.ExecuteScalarAsync();
            return result == null ? 0 : Convert.ToInt32(result);
        }

        /// <summary>
        /// Applies schema migrations. v1 is the initial schema; future versions add
        /// conditional ALTER TABLE blocks here, each bumping DbVersion when done.
        /// </summary>
        private static async Task MigrateAsync()
        {
            int version = await GetDatabaseVersion();

            // v1 → v2: three-slot alerts, dismissable/force-scroll flags, slot-state tracking.
            if (version < 2)
            {
                using var connection = await OpenConnectionAsync();
                foreach (var sql in new[]
                {
                    "ALTER TABLE Alerts ADD COLUMN Dismissable INTEGER NOT NULL DEFAULT 1;",
                    "ALTER TABLE Alerts ADD COLUMN ForceScroll INTEGER NOT NULL DEFAULT 0;",
                    "ALTER TABLE AlertDomains ADD COLUMN Slot INTEGER NOT NULL DEFAULT 1;",
                    "ALTER TABLE AlertDomains ADD COLUMN PushedValue TEXT;"
                })
                {
                    try
                    {
                        using var alter = connection.CreateCommand();
                        alter.CommandText = sql;
                        await alter.ExecuteNonQueryAsync();
                    }
                    catch
                    {
                        // Column already exists (fresh install created the v2 schema).
                    }
                }

                using var createSlots = connection.CreateCommand();
                createSlots.CommandText = @"
                    CREATE TABLE IF NOT EXISTS DomainSlots (
                        DomainId TEXT NOT NULL,
                        Slot INTEGER NOT NULL,
                        CurrentValue TEXT,
                        CurrentAlertId TEXT,
                        UpdatedAt TEXT,
                        PRIMARY KEY (DomainId, Slot)
                    );";
                await createSlots.ExecuteNonQueryAsync();
            }

            if (version < CurrentSchemaVersion)
            {
                using var connection = await OpenConnectionAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE DbVersion SET Version = @v WHERE Id = 1;";
                command.Parameters.AddWithValue("@v", CurrentSchemaVersion);
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
