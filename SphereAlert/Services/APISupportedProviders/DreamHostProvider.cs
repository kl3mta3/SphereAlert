using System.Text.Json;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.APISupportedProviders
{
    /// <summary>
    /// DreamHost DNS provider. Credential format: a single API key. Adapted from
    /// SphereSSL's DreamHostDNSHelper.
    ///
    /// TODO: DreamHost's API has no real per-record TTL or update operation — records
    /// are added/removed by exact value, and there is no zone-list endpoint. Upsert is
    /// implemented as remove-then-add, and ListZones derives registrable domains from
    /// the record list. This is best-effort given the API's limitations.
    /// </summary>
    public class DreamHostProvider : IAlertDnsProvider
    {
        private const string BaseUrl = "https://api.dreamhost.com/";

        private readonly string _apiKey;
        private readonly Logger _logger;

        public DreamHostProvider(string credentials, Logger logger)
        {
            _apiKey = credentials?.Trim() ?? string.Empty;
            _logger = logger;
        }

        private static HttpClient CreateClient() =>
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public async Task<ConnectionTestResult> TestConnectionAsync()
        {
            try
            {
                using var client = CreateClient();
                var response = await client.GetAsync(
                    $"{BaseUrl}?key={Uri.EscapeDataString(_apiKey)}&cmd=dns-list_records&format=json");
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    return ConnectionTestResult.Fail($"DreamHost returned {(int)response.StatusCode}.");

                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("result", out var result) &&
                    string.Equals(result.GetString(), "success", StringComparison.OrdinalIgnoreCase))
                {
                    return ConnectionTestResult.Ok();
                }
                return ConnectionTestResult.Fail("DreamHost rejected the API key.");
            }
            catch (Exception ex)
            {
                return ConnectionTestResult.Fail(ex.Message);
            }
        }

        public async Task<List<ZoneInfo>> ListZonesAsync()
        {
            var zones = new List<ZoneInfo>();
            try
            {
                using var client = CreateClient();
                var response = await client.GetAsync(
                    $"{BaseUrl}?key={Uri.EscapeDataString(_apiKey)}&cmd=dns-list_records&format=json");
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _ = _logger.Debug($"DreamHost ListZones failed: {response.StatusCode}");
                    return zones;
                }

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("data", out var data))
                    return zones;

                // DreamHost has no zone endpoint — derive distinct registrable domains
                // from the "zone" field of each record.
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var record in data.EnumerateArray())
                {
                    string zoneName = record.TryGetProperty("zone", out var z)
                        ? z.GetString() ?? string.Empty
                        : string.Empty;
                    if (!string.IsNullOrEmpty(zoneName) && seen.Add(zoneName))
                        zones.Add(new ZoneInfo { Name = zoneName, ZoneId = zoneName });
                }
            }
            catch (Exception ex)
            {
                _ = _logger.Debug($"DreamHost ListZones exception: {ex.Message}");
            }
            return zones;
        }

        public async Task<UpsertResult> UpsertTxtRecordAsync(string zone, string name, string value, int? ttlSeconds)
        {
            try
            {
                using var client = CreateClient();

                string recordName = $"{name}.{zone}".TrimEnd('.');

                // DreamHost has no update operation — remove any existing alert TXT,
                // then add the new value.
                await RemoveExistingAsync(client, recordName);

                var addUrl = $"{BaseUrl}?key={Uri.EscapeDataString(_apiKey)}&cmd=dns-add_record&format=json" +
                             $"&record={Uri.EscapeDataString(recordName)}" +
                             $"&type=TXT" +
                             $"&value={Uri.EscapeDataString(value)}";
                var response = await client.GetAsync(addUrl);
                var body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode && body.Contains("\"result\"") &&
                    body.Contains("success", StringComparison.OrdinalIgnoreCase))
                {
                    _ = _logger.Info($"DreamHost TXT upserted for {recordName}.");
                    return UpsertResult.Ok();
                }

                _ = _logger.Debug($"DreamHost upsert failed for {recordName}: {response.StatusCode}\n{body}");
                return UpsertResult.Fail($"DreamHost add_record failed: {ExtractError(body)}");
            }
            catch (Exception ex)
            {
                return UpsertResult.Fail(ex.Message);
            }
        }

        public async Task<DeleteResult> DeleteTxtRecordAsync(string zone, string name)
        {
            try
            {
                using var client = CreateClient();

                string recordName = $"{name}.{zone}".TrimEnd('.');
                bool removedAny = await RemoveExistingAsync(client, recordName);

                // RemoveExistingAsync returns true if all matching deletes succeeded
                // (or there were none). Treat that as success.
                return removedAny
                    ? DeleteResult.Ok()
                    : DeleteResult.Fail("DreamHost remove_record failed for one or more records.");
            }
            catch (Exception ex)
            {
                return DeleteResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Removes every TXT record matching <paramref name="recordName"/>.
        /// Returns true if no records remained or all deletes succeeded.
        /// </summary>
        private async Task<bool> RemoveExistingAsync(HttpClient client, string recordName)
        {
            var listResponse = await client.GetAsync(
                $"{BaseUrl}?key={Uri.EscapeDataString(_apiKey)}&cmd=dns-list_records&format=json");
            var listBody = await listResponse.Content.ReadAsStringAsync();
            if (!listResponse.IsSuccessStatusCode)
                return false;

            bool allSuccess = true;
            using var doc = JsonDocument.Parse(listBody);
            if (!doc.RootElement.TryGetProperty("data", out var data))
                return true;

            foreach (var record in data.EnumerateArray())
            {
                string recName = record.TryGetProperty("record", out var rn) ? rn.GetString() ?? string.Empty : string.Empty;
                string recType = record.TryGetProperty("type", out var rt) ? rt.GetString() ?? string.Empty : string.Empty;
                string recValue = record.TryGetProperty("value", out var rv) ? rv.GetString() ?? string.Empty : string.Empty;

                if (recType == "TXT" &&
                    string.Equals(recName, recordName, StringComparison.OrdinalIgnoreCase))
                {
                    // DreamHost removal requires the exact stored value.
                    var delUrl = $"{BaseUrl}?key={Uri.EscapeDataString(_apiKey)}&cmd=dns-remove_record&format=json" +
                                 $"&record={Uri.EscapeDataString(recName)}" +
                                 $"&type=TXT" +
                                 $"&value={Uri.EscapeDataString(recValue)}";
                    var delResponse = await client.GetAsync(delUrl);
                    var delBody = await delResponse.Content.ReadAsStringAsync();
                    if (delResponse.IsSuccessStatusCode &&
                        delBody.Contains("success", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = _logger.Info($"DreamHost removed existing TXT for {recName}.");
                    }
                    else
                    {
                        _ = _logger.Debug($"DreamHost remove failed for {recName}: {delResponse.StatusCode}\n{delBody}");
                        allSuccess = false;
                    }
                }
            }
            return allSuccess;
        }

        private static string ExtractError(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.String)
                {
                    return data.GetString() ?? "unknown error";
                }
            }
            catch
            {
                // fall through
            }
            return "unknown error";
        }
    }
}
