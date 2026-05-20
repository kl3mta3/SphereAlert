using System.Text;
using System.Text.Json;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.APISupportedProviders
{
    /// <summary>
    /// Hetzner DNS provider. Credential format: a single DNS API token, sent via
    /// the Auth-API-Token header. Adapted from SphereSSL's HetznerDNSHelper.
    /// </summary>
    public class HetznerProvider : IAlertDnsProvider
    {
        private const string BaseUrl = "https://dns.hetzner.com/api/v1";
        private const int MinTtl = 60;

        private readonly string _apiToken;
        private readonly Logger _logger;

        public HetznerProvider(string credentials, Logger logger)
        {
            _apiToken = credentials?.Trim() ?? string.Empty;
            _logger = logger;
        }

        private HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Add("Auth-API-Token", _apiToken);
            return client;
        }

        public async Task<ConnectionTestResult> TestConnectionAsync()
        {
            try
            {
                using var client = CreateClient();
                var response = await client.GetAsync($"{BaseUrl}/zones");
                if (!response.IsSuccessStatusCode)
                    return ConnectionTestResult.Fail($"Hetzner returned {(int)response.StatusCode}.");
                return ConnectionTestResult.Ok();
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
                var response = await client.GetAsync($"{BaseUrl}/zones");
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _ = _logger.Debug($"Hetzner ListZones failed: {response.StatusCode}");
                    return zones;
                }

                using var doc = JsonDocument.Parse(body);
                foreach (var zone in doc.RootElement.GetProperty("zones").EnumerateArray())
                {
                    zones.Add(new ZoneInfo
                    {
                        Name = zone.GetProperty("name").GetString() ?? string.Empty,
                        ZoneId = zone.GetProperty("id").GetString()
                    });
                }
            }
            catch (Exception ex)
            {
                _ = _logger.Debug($"Hetzner ListZones exception: {ex.Message}");
            }
            return zones;
        }

        public async Task<UpsertResult> UpsertTxtRecordAsync(string zone, string name, string value, int? ttlSeconds)
        {
            try
            {
                using var client = CreateClient();

                string? zoneId = await GetZoneIdAsync(client, zone);
                if (string.IsNullOrEmpty(zoneId))
                    return UpsertResult.Fail($"No Hetzner zone found for '{zone}'.");

                int ttl = Math.Max(ttlSeconds ?? 120, MinTtl);
                string? recordId = await GetTxtRecordIdAsync(client, zoneId, name);

                var payload = JsonSerializer.Serialize(new
                {
                    zone_id = zoneId,
                    type = "TXT",
                    name,
                    value,
                    ttl
                });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                HttpResponseMessage response = string.IsNullOrEmpty(recordId)
                    ? await client.PostAsync($"{BaseUrl}/records", content)
                    : await client.PutAsync($"{BaseUrl}/records/{recordId}", content);

                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    _ = _logger.Info($"Hetzner TXT upserted for {name}.{zone}.");
                    return UpsertResult.Ok();
                }

                _ = _logger.Debug($"Hetzner upsert failed for {name}.{zone}: {response.StatusCode}\n{body}");
                return UpsertResult.Fail($"Hetzner returned {(int)response.StatusCode}.");
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

                string? zoneId = await GetZoneIdAsync(client, zone);
                if (string.IsNullOrEmpty(zoneId))
                    return DeleteResult.Fail($"No Hetzner zone found for '{zone}'.");

                string? recordId = await GetTxtRecordIdAsync(client, zoneId, name);
                if (string.IsNullOrEmpty(recordId))
                    return DeleteResult.Ok(); // Nothing to remove.

                var response = await client.DeleteAsync($"{BaseUrl}/records/{recordId}");
                if (response.IsSuccessStatusCode)
                    return DeleteResult.Ok();

                var body = await response.Content.ReadAsStringAsync();
                _ = _logger.Debug($"Hetzner delete failed for {name}.{zone}: {response.StatusCode}\n{body}");
                return DeleteResult.Fail($"Hetzner returned {(int)response.StatusCode}.");
            }
            catch (Exception ex)
            {
                return DeleteResult.Fail(ex.Message);
            }
        }

        private static async Task<string?> GetZoneIdAsync(HttpClient client, string zone)
        {
            var response = await client.GetAsync($"{BaseUrl}/zones?name={Uri.EscapeDataString(zone)}");
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("zones", out var zones))
            {
                foreach (var z in zones.EnumerateArray())
                {
                    if (string.Equals(z.GetProperty("name").GetString(), zone, StringComparison.OrdinalIgnoreCase))
                        return z.GetProperty("id").GetString();
                }
            }
            return null;
        }

        private static async Task<string?> GetTxtRecordIdAsync(HttpClient client, string zoneId, string name)
        {
            var response = await client.GetAsync($"{BaseUrl}/records?zone_id={zoneId}");
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("records", out var records))
            {
                foreach (var rec in records.EnumerateArray())
                {
                    if (rec.GetProperty("type").GetString() == "TXT" &&
                        string.Equals(rec.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return rec.GetProperty("id").GetString();
                    }
                }
            }
            return null;
        }
    }
}
