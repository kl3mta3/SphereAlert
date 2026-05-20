using Microsoft.Data.Sqlite;
using SphereAlert.Data.Database;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Security;

namespace SphereAlert.Data.Repositories
{
    /// <summary>
    /// DNS provider credential storage. Credentials are encrypted with AES-256-GCM
    /// before they touch the database and decrypted on read.
    /// </summary>
    public class ProviderRepository
    {
        public async Task<List<DnsProviderRecord>> GetAllAsync()
        {
            var results = new List<DnsProviderRecord>();
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Providers ORDER BY DisplayName;";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                results.Add(Map(reader));
            return results;
        }

        public async Task<DnsProviderRecord?> GetByIdAsync(string providerId)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Providers WHERE ProviderId = @providerId LIMIT 1;";
            command.Parameters.AddWithValue("@providerId", providerId);
            using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? Map(reader) : null;
        }

        public async Task InsertAsync(DnsProviderRecord provider)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Providers (ProviderId, Type, DisplayName, EncryptedCredentials, CreatedAt, LastTestedAt, LastTestResult)
                VALUES (@providerId, @type, @displayName, @credentials, @createdAt, @lastTestedAt, @lastTestResult);";
            command.Parameters.AddWithValue("@providerId", provider.ProviderId);
            command.Parameters.AddWithValue("@type", provider.Type);
            command.Parameters.AddWithValue("@displayName", provider.DisplayName);
            command.Parameters.AddWithValue("@credentials", CryptoService.Encrypt(provider.Credentials));
            command.Parameters.AddWithValue("@createdAt", provider.CreatedAt.ToString("o"));
            command.Parameters.AddWithValue("@lastTestedAt",
                provider.LastTestedAt.HasValue ? provider.LastTestedAt.Value.ToString("o") : DBNull.Value);
            command.Parameters.AddWithValue("@lastTestResult", provider.LastTestResult ?? string.Empty);
            await command.ExecuteNonQueryAsync();
        }

        public async Task UpdateAsync(DnsProviderRecord provider)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Providers
                SET Type = @type, DisplayName = @displayName, EncryptedCredentials = @credentials
                WHERE ProviderId = @providerId;";
            command.Parameters.AddWithValue("@type", provider.Type);
            command.Parameters.AddWithValue("@displayName", provider.DisplayName);
            command.Parameters.AddWithValue("@credentials", CryptoService.Encrypt(provider.Credentials));
            command.Parameters.AddWithValue("@providerId", provider.ProviderId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task UpdateTestResultAsync(string providerId, string result)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Providers SET LastTestedAt = @testedAt, LastTestResult = @result
                WHERE ProviderId = @providerId;";
            command.Parameters.AddWithValue("@testedAt", DateTime.UtcNow.ToString("o"));
            command.Parameters.AddWithValue("@result", result);
            command.Parameters.AddWithValue("@providerId", providerId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task DeleteAsync(string providerId)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Providers WHERE ProviderId = @providerId;";
            command.Parameters.AddWithValue("@providerId", providerId);
            await command.ExecuteNonQueryAsync();
        }

        private static DnsProviderRecord Map(SqliteDataReader reader)
        {
            int testedAtOrdinal = reader.GetOrdinal("LastTestedAt");
            int testResultOrdinal = reader.GetOrdinal("LastTestResult");
            return new DnsProviderRecord
            {
                ProviderId = reader.GetString(reader.GetOrdinal("ProviderId")),
                Type = reader.GetString(reader.GetOrdinal("Type")),
                DisplayName = reader.GetString(reader.GetOrdinal("DisplayName")),
                Credentials = CryptoService.Decrypt(reader.GetString(reader.GetOrdinal("EncryptedCredentials"))),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt")),
                    null, System.Globalization.DateTimeStyles.RoundtripKind),
                LastTestedAt = reader.IsDBNull(testedAtOrdinal)
                    ? null
                    : DateTime.Parse(reader.GetString(testedAtOrdinal), null, System.Globalization.DateTimeStyles.RoundtripKind),
                LastTestResult = reader.IsDBNull(testResultOrdinal) ? string.Empty : reader.GetString(testResultOrdinal)
            };
        }
    }
}
