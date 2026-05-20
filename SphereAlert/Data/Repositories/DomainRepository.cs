using Microsoft.Data.Sqlite;
using SphereAlert.Data.Database;
using SphereAlert.Models.DNSModels;

namespace SphereAlert.Data.Repositories
{
    public class DomainRepository
    {
        private const string SelectWithDetail = @"
            SELECT d.DomainId, d.Name, d.ProviderId, d.LastSyncedAt, d.Status,
                   d.ScriptDetectedAt, d.ScriptStatus,
                   p.DisplayName AS ProviderDisplayName, p.Type AS ProviderType,
                   (SELECT a.Level FROM AlertDomains ad
                      JOIN Alerts a ON a.AlertId = ad.AlertId
                      WHERE ad.DomainId = d.DomainId AND ad.PushStatus = 'success' AND a.Status = 'active'
                      ORDER BY a.CreatedAt DESC LIMIT 1) AS ActiveAlertLevel
            FROM Domains d
            LEFT JOIN Providers p ON p.ProviderId = d.ProviderId";

        public async Task<List<DomainRecord>> GetAllWithDetailAsync()
        {
            var results = new List<DomainRecord>();
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = SelectWithDetail + " ORDER BY d.Name;";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                results.Add(Map(reader));
            return results;
        }

        public async Task<DomainRecord?> GetByIdAsync(string domainId)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = SelectWithDetail + " WHERE d.DomainId = @domainId LIMIT 1;";
            command.Parameters.AddWithValue("@domainId", domainId);
            using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? Map(reader) : null;
        }

        public async Task<List<DomainRecord>> GetByIdsAsync(IEnumerable<string> domainIds)
        {
            var ids = domainIds.Distinct().ToList();
            var results = new List<DomainRecord>();
            if (ids.Count == 0)
                return results;

            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            var placeholders = new List<string>();
            for (int i = 0; i < ids.Count; i++)
            {
                placeholders.Add($"@id{i}");
                command.Parameters.AddWithValue($"@id{i}", ids[i]);
            }
            command.CommandText = SelectWithDetail +
                $" WHERE d.DomainId IN ({string.Join(",", placeholders)}) ORDER BY d.Name;";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                results.Add(Map(reader));
            return results;
        }

        public async Task<DomainRecord?> GetByNameAndProviderAsync(string name, string providerId)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = SelectWithDetail +
                " WHERE d.Name = @name AND d.ProviderId = @providerId LIMIT 1;";
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@providerId", providerId);
            using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? Map(reader) : null;
        }

        public async Task<int> CountByProviderAsync(string providerId)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM Domains WHERE ProviderId = @providerId;";
            command.Parameters.AddWithValue("@providerId", providerId);
            return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
        }

        public async Task<int> CountAsync()
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM Domains;";
            return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
        }

        public async Task InsertAsync(DomainRecord domain)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Domains (DomainId, Name, ProviderId, LastSyncedAt, Status, ScriptDetectedAt, ScriptStatus)
                VALUES (@domainId, @name, @providerId, @lastSyncedAt, @status, @scriptDetectedAt, @scriptStatus);";
            command.Parameters.AddWithValue("@domainId", domain.DomainId);
            command.Parameters.AddWithValue("@name", domain.Name);
            command.Parameters.AddWithValue("@providerId", domain.ProviderId);
            command.Parameters.AddWithValue("@lastSyncedAt",
                domain.LastSyncedAt.HasValue ? domain.LastSyncedAt.Value.ToString("o") : DBNull.Value);
            command.Parameters.AddWithValue("@status", domain.Status);
            command.Parameters.AddWithValue("@scriptDetectedAt",
                domain.ScriptDetectedAt.HasValue ? domain.ScriptDetectedAt.Value.ToString("o") : DBNull.Value);
            command.Parameters.AddWithValue("@scriptStatus", domain.ScriptStatus);
            await command.ExecuteNonQueryAsync();
        }

        public async Task UpdateAsync(DomainRecord domain)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Domains SET Name = @name, ProviderId = @providerId
                WHERE DomainId = @domainId;";
            command.Parameters.AddWithValue("@name", domain.Name);
            command.Parameters.AddWithValue("@providerId", domain.ProviderId);
            command.Parameters.AddWithValue("@domainId", domain.DomainId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task UpdateSyncStatusAsync(string domainId, string status)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Domains SET Status = @status, LastSyncedAt = @syncedAt
                WHERE DomainId = @domainId;";
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@syncedAt", DateTime.UtcNow.ToString("o"));
            command.Parameters.AddWithValue("@domainId", domainId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task UpdateScriptStatusAsync(string domainId, string scriptStatus)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Domains SET ScriptStatus = @scriptStatus, ScriptDetectedAt = @detectedAt
                WHERE DomainId = @domainId;";
            command.Parameters.AddWithValue("@scriptStatus", scriptStatus);
            command.Parameters.AddWithValue("@detectedAt", DateTime.UtcNow.ToString("o"));
            command.Parameters.AddWithValue("@domainId", domainId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task DeleteAsync(string domainId)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Domains WHERE DomainId = @domainId;";
            command.Parameters.AddWithValue("@domainId", domainId);
            await command.ExecuteNonQueryAsync();
        }

        // --- Slot state ---------------------------------------------------------

        /// <summary>Records the TXT value currently written to a (domain, slot).</summary>
        public async Task UpsertSlotStateAsync(string domainId, int slot, string? currentValue, string? currentAlertId)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO DomainSlots (DomainId, Slot, CurrentValue, CurrentAlertId, UpdatedAt)
                VALUES (@domainId, @slot, @value, @alertId, @updatedAt)
                ON CONFLICT(DomainId, Slot) DO UPDATE SET
                    CurrentValue = excluded.CurrentValue,
                    CurrentAlertId = excluded.CurrentAlertId,
                    UpdatedAt = excluded.UpdatedAt;";
            command.Parameters.AddWithValue("@domainId", domainId);
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@value", (object?)currentValue ?? DBNull.Value);
            command.Parameters.AddWithValue("@alertId", (object?)currentAlertId ?? DBNull.Value);
            command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o"));
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>Returns the known state of all three slots for a domain (slots with no writes are omitted).</summary>
        public async Task<List<DomainSlot>> GetSlotStatesAsync(string domainId)
        {
            var results = new List<DomainSlot>();
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM DomainSlots WHERE DomainId = @domainId ORDER BY Slot;";
            command.Parameters.AddWithValue("@domainId", domainId);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int valueOrdinal = reader.GetOrdinal("CurrentValue");
                int alertOrdinal = reader.GetOrdinal("CurrentAlertId");
                results.Add(new DomainSlot
                {
                    DomainId = reader.GetString(reader.GetOrdinal("DomainId")),
                    Slot = (int)reader.GetInt64(reader.GetOrdinal("Slot")),
                    CurrentValue = reader.IsDBNull(valueOrdinal) ? null : reader.GetString(valueOrdinal),
                    CurrentAlertId = reader.IsDBNull(alertOrdinal) ? null : reader.GetString(alertOrdinal),
                    UpdatedAt = ParseNullable(reader, "UpdatedAt")
                });
            }
            return results;
        }

        private static DateTime? ParseNullable(SqliteDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal)
                ? null
                : DateTime.Parse(reader.GetString(ordinal), null, System.Globalization.DateTimeStyles.RoundtripKind);
        }

        private static DomainRecord Map(SqliteDataReader reader)
        {
            int providerNameOrdinal = reader.GetOrdinal("ProviderDisplayName");
            int providerTypeOrdinal = reader.GetOrdinal("ProviderType");
            int activeLevelOrdinal = reader.GetOrdinal("ActiveAlertLevel");
            return new DomainRecord
            {
                DomainId = reader.GetString(reader.GetOrdinal("DomainId")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                ProviderId = reader.GetString(reader.GetOrdinal("ProviderId")),
                LastSyncedAt = ParseNullable(reader, "LastSyncedAt"),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                ScriptDetectedAt = ParseNullable(reader, "ScriptDetectedAt"),
                ScriptStatus = reader.GetString(reader.GetOrdinal("ScriptStatus")),
                ProviderDisplayName = reader.IsDBNull(providerNameOrdinal) ? string.Empty : reader.GetString(providerNameOrdinal),
                ProviderType = reader.IsDBNull(providerTypeOrdinal) ? string.Empty : reader.GetString(providerTypeOrdinal),
                ActiveAlertLevel = reader.IsDBNull(activeLevelOrdinal) ? null : reader.GetString(activeLevelOrdinal)
            };
        }
    }
}
