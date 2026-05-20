using Microsoft.Data.Sqlite;
using SphereAlert.Data.Database;
using SphereAlert.Models.AlertModels;

namespace SphereAlert.Data.Repositories
{
    public class AlertRepository
    {
        // --- Alerts -------------------------------------------------------------

        public async Task InsertAlertAsync(Alert alert)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Alerts (AlertId, Level, Message, EndAt, Status, CreatedAt, CreatedByUserId, ExpiredAt, Dismissable, ForceScroll)
                VALUES (@alertId, @level, @message, @endAt, @status, @createdAt, @createdBy, @expiredAt, @dismissable, @forceScroll);";
            command.Parameters.AddWithValue("@alertId", alert.AlertId);
            command.Parameters.AddWithValue("@level", alert.Level);
            command.Parameters.AddWithValue("@message", alert.Message);
            command.Parameters.AddWithValue("@endAt", alert.EndAt.HasValue ? alert.EndAt.Value.ToString("o") : DBNull.Value);
            command.Parameters.AddWithValue("@status", alert.Status);
            command.Parameters.AddWithValue("@createdAt", alert.CreatedAt.ToString("o"));
            command.Parameters.AddWithValue("@createdBy", string.IsNullOrEmpty(alert.CreatedByUserId) ? DBNull.Value : alert.CreatedByUserId);
            command.Parameters.AddWithValue("@expiredAt", alert.ExpiredAt.HasValue ? alert.ExpiredAt.Value.ToString("o") : DBNull.Value);
            command.Parameters.AddWithValue("@dismissable", alert.Dismissable ? 1 : 0);
            command.Parameters.AddWithValue("@forceScroll", alert.ForceScroll ? 1 : 0);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<Alert?> GetByIdAsync(string alertId)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Alerts WHERE AlertId = @alertId LIMIT 1;";
            command.Parameters.AddWithValue("@alertId", alertId);
            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;
            var alert = MapAlert(reader);
            await reader.CloseAsync();
            alert.Domains = await GetAlertDomainsAsync(alertId);
            return alert;
        }

        public async Task<List<Alert>> GetActiveAsync()
        {
            var alerts = new List<Alert>();
            using (var connection = await DatabaseManager.OpenConnectionAsync())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Alerts WHERE Status = 'active' ORDER BY CreatedAt DESC;";
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    alerts.Add(MapAlert(reader));
            }
            foreach (var alert in alerts)
                alert.Domains = await GetAlertDomainsAsync(alert.AlertId);
            return alerts;
        }

        public async Task<List<Alert>> GetAlertsToExpireAsync()
        {
            var alerts = new List<Alert>();
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT * FROM Alerts
                WHERE Status = 'active' AND EndAt IS NOT NULL AND EndAt <= @now
                ORDER BY EndAt;";
            command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                alerts.Add(MapAlert(reader));
            return alerts;
        }

        public async Task<int> CountActiveAsync()
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM Alerts WHERE Status = 'active';";
            return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
        }

        public async Task UpdateAlertContentAsync(
            string alertId, string level, string message, DateTime? endAt, bool dismissable, bool forceScroll)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Alerts
                SET Level = @level, Message = @message, EndAt = @endAt,
                    Dismissable = @dismissable, ForceScroll = @forceScroll
                WHERE AlertId = @alertId;";
            command.Parameters.AddWithValue("@level", level);
            command.Parameters.AddWithValue("@message", message);
            command.Parameters.AddWithValue("@endAt", endAt.HasValue ? endAt.Value.ToString("o") : DBNull.Value);
            command.Parameters.AddWithValue("@dismissable", dismissable ? 1 : 0);
            command.Parameters.AddWithValue("@forceScroll", forceScroll ? 1 : 0);
            command.Parameters.AddWithValue("@alertId", alertId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task SetStatusAsync(string alertId, string status, DateTime? expiredAt)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Alerts SET Status = @status, ExpiredAt = @expiredAt
                WHERE AlertId = @alertId;";
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@expiredAt", expiredAt.HasValue ? expiredAt.Value.ToString("o") : DBNull.Value);
            command.Parameters.AddWithValue("@alertId", alertId);
            await command.ExecuteNonQueryAsync();
        }

        // --- AlertDomains -------------------------------------------------------

        public async Task InsertAlertDomainAsync(AlertDomain alertDomain)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO AlertDomains (AlertId, DomainId, Slot, PushStatus, PushError, PushedValue, PushedAt)
                VALUES (@alertId, @domainId, @slot, @pushStatus, @pushError, @pushedValue, @pushedAt);";
            command.Parameters.AddWithValue("@alertId", alertDomain.AlertId);
            command.Parameters.AddWithValue("@domainId", alertDomain.DomainId);
            command.Parameters.AddWithValue("@slot", alertDomain.Slot);
            command.Parameters.AddWithValue("@pushStatus", alertDomain.PushStatus);
            command.Parameters.AddWithValue("@pushError", (object?)alertDomain.PushError ?? DBNull.Value);
            command.Parameters.AddWithValue("@pushedValue", (object?)alertDomain.PushedValue ?? DBNull.Value);
            command.Parameters.AddWithValue("@pushedAt",
                alertDomain.PushedAt.HasValue ? alertDomain.PushedAt.Value.ToString("o") : DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<List<AlertDomain>> GetAlertDomainsAsync(string alertId)
        {
            var results = new List<AlertDomain>();
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT ad.Id, ad.AlertId, ad.DomainId, ad.Slot, ad.PushStatus, ad.PushError,
                       ad.PushedValue, ad.PushedAt,
                       COALESCE(d.Name, '(deleted domain)') AS DomainName
                FROM AlertDomains ad
                LEFT JOIN Domains d ON d.DomainId = ad.DomainId
                WHERE ad.AlertId = @alertId
                ORDER BY DomainName, ad.Slot;";
            command.Parameters.AddWithValue("@alertId", alertId);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                results.Add(MapAlertDomain(reader));
            return results;
        }

        public async Task UpdateAlertDomainResultAsync(long id, string pushStatus, string? pushError, string? pushedValue)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE AlertDomains
                SET PushStatus = @pushStatus, PushError = @pushError, PushedValue = @pushedValue, PushedAt = @pushedAt
                WHERE Id = @id;";
            command.Parameters.AddWithValue("@pushStatus", pushStatus);
            command.Parameters.AddWithValue("@pushError", (object?)pushError ?? DBNull.Value);
            command.Parameters.AddWithValue("@pushedValue", (object?)pushedValue ?? DBNull.Value);
            command.Parameters.AddWithValue("@pushedAt", DateTime.UtcNow.ToString("o"));
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync();
        }

        // --- Mapping ------------------------------------------------------------

        private static DateTime? ParseNullable(SqliteDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal)
                ? null
                : DateTime.Parse(reader.GetString(ordinal), null, System.Globalization.DateTimeStyles.RoundtripKind);
        }

        private static Alert MapAlert(SqliteDataReader reader)
        {
            int createdByOrdinal = reader.GetOrdinal("CreatedByUserId");
            return new Alert
            {
                AlertId = reader.GetString(reader.GetOrdinal("AlertId")),
                Level = reader.GetString(reader.GetOrdinal("Level")),
                Message = reader.GetString(reader.GetOrdinal("Message")),
                EndAt = ParseNullable(reader, "EndAt"),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt")),
                    null, System.Globalization.DateTimeStyles.RoundtripKind),
                CreatedByUserId = reader.IsDBNull(createdByOrdinal) ? string.Empty : reader.GetString(createdByOrdinal),
                ExpiredAt = ParseNullable(reader, "ExpiredAt"),
                Dismissable = reader.GetInt64(reader.GetOrdinal("Dismissable")) != 0,
                ForceScroll = reader.GetInt64(reader.GetOrdinal("ForceScroll")) != 0
            };
        }

        private static AlertDomain MapAlertDomain(SqliteDataReader reader)
        {
            int errorOrdinal = reader.GetOrdinal("PushError");
            int valueOrdinal = reader.GetOrdinal("PushedValue");
            return new AlertDomain
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                AlertId = reader.GetString(reader.GetOrdinal("AlertId")),
                DomainId = reader.GetString(reader.GetOrdinal("DomainId")),
                Slot = (int)reader.GetInt64(reader.GetOrdinal("Slot")),
                PushStatus = reader.GetString(reader.GetOrdinal("PushStatus")),
                PushError = reader.IsDBNull(errorOrdinal) ? null : reader.GetString(errorOrdinal),
                PushedValue = reader.IsDBNull(valueOrdinal) ? null : reader.GetString(valueOrdinal),
                PushedAt = ParseNullable(reader, "PushedAt"),
                DomainName = reader.GetString(reader.GetOrdinal("DomainName"))
            };
        }
    }
}
