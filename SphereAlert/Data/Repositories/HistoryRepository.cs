using Microsoft.Data.Sqlite;
using SphereAlert.Data.Database;
using SphereAlert.Models.AlertModels;

namespace SphereAlert.Data.Repositories
{
    public class HistoryRepository
    {
        public async Task InsertAsync(HistoryEntry entry)
        {
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO History (EventType, AlertId, DomainId, UserId, Timestamp, DetailsJson)
                VALUES (@eventType, @alertId, @domainId, @userId, @timestamp, @detailsJson);";
            command.Parameters.AddWithValue("@eventType", entry.EventType);
            command.Parameters.AddWithValue("@alertId", (object?)entry.AlertId ?? DBNull.Value);
            command.Parameters.AddWithValue("@domainId", (object?)entry.DomainId ?? DBNull.Value);
            command.Parameters.AddWithValue("@userId", (object?)entry.UserId ?? DBNull.Value);
            command.Parameters.AddWithValue("@timestamp", entry.Timestamp.ToString("o"));
            command.Parameters.AddWithValue("@detailsJson", entry.DetailsJson ?? "{}");
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>Returns history entries newest-first, with optional filters.</summary>
        public async Task<List<HistoryEntry>> GetAsync(
            string? domainId = null, string? eventType = null, string? level = null,
            DateTime? from = null, DateTime? to = null)
        {
            var results = new List<HistoryEntry>();
            using var connection = await DatabaseManager.OpenConnectionAsync();
            using var command = connection.CreateCommand();

            var where = new List<string>();
            if (!string.IsNullOrWhiteSpace(domainId))
            {
                where.Add("h.DomainId = @domainId");
                command.Parameters.AddWithValue("@domainId", domainId);
            }
            if (!string.IsNullOrWhiteSpace(eventType))
            {
                where.Add("h.EventType = @eventType");
                command.Parameters.AddWithValue("@eventType", eventType);
            }
            if (!string.IsNullOrWhiteSpace(level))
            {
                where.Add("a.Level = @level");
                command.Parameters.AddWithValue("@level", level);
            }
            if (from.HasValue)
            {
                where.Add("h.Timestamp >= @from");
                command.Parameters.AddWithValue("@from", from.Value.ToString("o"));
            }
            if (to.HasValue)
            {
                where.Add("h.Timestamp <= @to");
                command.Parameters.AddWithValue("@to", to.Value.ToString("o"));
            }

            command.CommandText = @"
                SELECT h.Id, h.EventType, h.AlertId, h.DomainId, h.UserId, h.Timestamp, h.DetailsJson,
                       COALESCE(d.Name, '') AS DomainName,
                       COALESCE(a.Level, '') AS AlertLevel,
                       COALESCE(a.Message, '') AS AlertMessage
                FROM History h
                LEFT JOIN Domains d ON d.DomainId = h.DomainId
                LEFT JOIN Alerts a ON a.AlertId = h.AlertId"
                + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : string.Empty)
                + " ORDER BY h.Timestamp DESC, h.Id DESC LIMIT 1000;";

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                results.Add(Map(reader));
            return results;
        }

        private static HistoryEntry Map(SqliteDataReader reader)
        {
            int alertIdOrdinal = reader.GetOrdinal("AlertId");
            int domainIdOrdinal = reader.GetOrdinal("DomainId");
            int userIdOrdinal = reader.GetOrdinal("UserId");
            int detailsOrdinal = reader.GetOrdinal("DetailsJson");
            return new HistoryEntry
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                EventType = reader.GetString(reader.GetOrdinal("EventType")),
                AlertId = reader.IsDBNull(alertIdOrdinal) ? null : reader.GetString(alertIdOrdinal),
                DomainId = reader.IsDBNull(domainIdOrdinal) ? null : reader.GetString(domainIdOrdinal),
                UserId = reader.IsDBNull(userIdOrdinal) ? null : reader.GetString(userIdOrdinal),
                Timestamp = DateTime.Parse(reader.GetString(reader.GetOrdinal("Timestamp")),
                    null, System.Globalization.DateTimeStyles.RoundtripKind),
                DetailsJson = reader.IsDBNull(detailsOrdinal) ? "{}" : reader.GetString(detailsOrdinal),
                DomainName = reader.GetString(reader.GetOrdinal("DomainName")),
                AlertLevel = reader.GetString(reader.GetOrdinal("AlertLevel")),
                AlertMessage = reader.GetString(reader.GetOrdinal("AlertMessage"))
            };
        }
    }
}
