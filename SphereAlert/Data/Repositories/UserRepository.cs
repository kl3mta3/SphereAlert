using Microsoft.Data.Sqlite;
using SphereAlert.Data.Database;
using SphereAlert.Models.UserModels;

namespace SphereAlert.Data.Repositories
{
    public class UserRepository
    {
        public async Task<long> CountUsersAsync()
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM Users;";
            return (long)(await command.ExecuteScalarAsync() ?? 0L);
        }

        /// <summary>True while a seed account still has its forced credential change pending —
        /// i.e. the default admin/pass123 login has not been changed yet.</summary>
        public async Task<bool> IsFirstRunAsync()
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM Users WHERE MustChangePassword = 1;";
            return (long)(await command.ExecuteScalarAsync() ?? 0L) > 0;
        }

        public async Task<User?> GetUserByUsernameAsync(string username)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Users WHERE Username = @username LIMIT 1;";
            command.Parameters.AddWithValue("@username", username);
            using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? Map(reader) : null;
        }

        public async Task<User?> GetUserByIdAsync(string userId)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Users WHERE UserId = @userId LIMIT 1;";
            command.Parameters.AddWithValue("@userId", userId);
            using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? Map(reader) : null;
        }

        public async Task InsertUserAsync(User user)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Users (UserId, Username, PasswordHash, MustChangePassword, CreatedAt)
                VALUES (@userId, @username, @passwordHash, @mustChange, @createdAt);";
            command.Parameters.AddWithValue("@userId", user.UserId);
            command.Parameters.AddWithValue("@username", user.Username);
            command.Parameters.AddWithValue("@passwordHash", user.PasswordHash);
            command.Parameters.AddWithValue("@mustChange", user.MustChangePassword ? 1 : 0);
            command.Parameters.AddWithValue("@createdAt", user.CreatedAt.ToString("o"));
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>Updates the username and password together, and clears the
        /// forced-change flag. Used by the change-password / first-run setup page.</summary>
        public async Task UpdateAccountAsync(string userId, string username, string passwordHash)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Users
                SET Username = @username, PasswordHash = @passwordHash, MustChangePassword = 0
                WHERE UserId = @userId;";
            command.Parameters.AddWithValue("@username", username);
            command.Parameters.AddWithValue("@passwordHash", passwordHash);
            command.Parameters.AddWithValue("@userId", userId);
            await command.ExecuteNonQueryAsync();
        }

        private static User Map(SqliteDataReader reader) => new()
        {
            UserId = reader.GetString(reader.GetOrdinal("UserId")),
            Username = reader.GetString(reader.GetOrdinal("Username")),
            PasswordHash = reader.GetString(reader.GetOrdinal("PasswordHash")),
            MustChangePassword = reader.GetInt64(reader.GetOrdinal("MustChangePassword")) != 0,
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt")),
                null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }
}
