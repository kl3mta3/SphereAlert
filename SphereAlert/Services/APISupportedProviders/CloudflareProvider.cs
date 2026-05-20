using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.APISupportedProviders
{
    /// <summary>
    /// Cloudflare DNS provider. Credential format: a single API Token with
    /// Zone:DNS:Edit permission (Bearer token). Adapted from SphereSSL's CloudflareHelper.
    /// </summary>
    public class CloudflareProvider : IAlertDnsProvider
    {
        private const string BaseUrl = "https://api.cloudflare.com/client/v4";
        private const int MinTtl = 60;

        private readonly string _apiToken;
        private readonly Logger _logger;

        public CloudflareProvider(string credentials, Logger logger)
        {
            _apiToken = credentials?.Trim() ?? string.Empty;
            _logger = logger;
        }

        private HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
            return client;
        }

        public async Task<ConnectionTestResult> TestConnectionAsync()
        {
            try
            {
                using var client = CreateClient();
                var response = await client.GetAsync($"{BaseUrl}/user/tokens/verify");
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    return ConnectionTestResult.Fail($"Cloudflare returned {(int)response.StatusCode}.");

                using var doc = JsonDocument.Parse(body);
                bool success = doc.RootElement.GetProperty("success").GetBoolean();
                return success
                    ? ConnectionTestResult.Ok()
                    : ConnectionTestResult.Fail("Cloudflare rejected the API token.");
            }
            catch (Exception ex)
            {
                return ConnectionTestResult.Fail(ex.Message);
            }
        }

        public async Task<List<ZoneInfo>> ListZonesAsync()
        {
            var zones = new List<ZoneInfo>();
            using var client = CreateClient();

            int page = 1;
            while (true)
            {
                var response = await client.GetAsync($"{BaseUrl}/zones?per_page=50&page={page}");
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _ = _logger.Debug($"Cloudflare ListZones failed: {response.StatusCode}");
                    break;
                }

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (!root.GetProperty("success").GetBoolean())
                    break;

                foreach (var zone in root.GetProperty("result").EnumerateArray())
                {
                    zones.Add(new ZoneInfo
                    {
                        Name = zone.GetProperty("name").GetString() ?? string.Empty,
                        ZoneId = zone.GetProperty("id").GetString()
                    });
                }

                int totalPages = 1;
                if (root.TryGetProperty("result_info", out var info) &&
                    info.TryGetProperty("total_pages", out var tp))
                {
                    totalPages = tp.GetInt32();
                }
                if (page >= totalPages)
                    break;
                page++;
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
                    return UpsertResult.Fail($"No Cloudflare zone found for '{zone}'.");

                string recordName = $"{name}.{zone}";
                string? recordId = await GetTxtRecordIdAsync(client, zoneId, recordName);
                int ttl = Math.Max(ttlSeconds ?? MinTtl, MinTtl);

                var payload = JsonSerializer.Serialize(new
                {
                    type = "TXT",
                    name = recordName,
                    content = value,
                    ttl
                });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                HttpResponseMessage response = string.IsNullOrEmpty(recordId)
                    ? await client.PostAsync($"{BaseUrl}/zones/{zoneId}/dns_records", content)
                    : await client.PutAsync($"{BaseUrl}/zones/{zoneId}/dns_records/{recordId}", content);

                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    _ = _logger.Info($"Cloudflare TXT upserted for {recordName}.");
                    return UpsertResult.Ok();
                }

                _ = _logger.Debug($"Cloudflare upsert failed for {recordName}: {response.StatusCode}\n{body}");
                return UpsertResult.Fail($"Cloudflare returned {(int)response.StatusCode}: {ExtractError(body)}");
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
                    return DeleteResult.Fail($"No Cloudflare zone found for '{zone}'.");

                string recordName = $"{name}.{zone}";
                string? recordId = await GetTxtRecordIdAsync(client, zoneId, recordName);
                if (string.IsNullOrEmpty(recordId))
                    return DeleteResult.Ok(); // Nothing to remove.

                var response = await client.DeleteAsync($"{BaseUrl}/zones/{zoneId}/dns_records/{recordId}");
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                    return DeleteResult.Ok();

                return DeleteResult.Fail($"Cloudflare returned {(int)response.StatusCode}: {ExtractError(body)}");
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
            var root = doc.RootElement;
            if (root.GetProperty("success").GetBoolean() &&
                root.GetProperty("result").GetArrayLength() > 0)
            {
                return root.GetProperty("result")[0].GetProperty("id").GetString();
            }
            return null;
        }

        private static async Task<string?> GetTxtRecordIdAsync(HttpClient client, string zoneId, string recordName)
        {
            var response = await client.GetAsync(
                $"{BaseUrl}/zones/{zoneId}/dns_records?type=TXT&name={Uri.EscapeDataString(recordName)}");
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.GetProperty("success").GetBoolean() &&
                root.GetProperty("result").GetArrayLength() > 0)
            {
                return root.GetProperty("result")[0].GetProperty("id").GetString();
            }
            return null;
        }

        private static string ExtractError(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("errors", out var errors) &&
                    errors.GetArrayLength() > 0)
                {
                    return errors[0].GetProperty("message").GetString() ?? "unknown error";
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
