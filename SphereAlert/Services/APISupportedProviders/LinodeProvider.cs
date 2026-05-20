using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.APISupportedProviders
{
    /// <summary>
    /// Linode DNS provider. Credential format: a single Personal Access Token
    /// (Bearer token). Adapted from SphereSSL's LinodeDNSHelper.
    /// </summary>
    public class LinodeProvider : IAlertDnsProvider
    {
        private const string BaseUrl = "https://api.linode.com/v4";
        private const int MinTtl = 30;

        private readonly string _apiToken;
        private readonly Logger _logger;

        public LinodeProvider(string credentials, Logger logger)
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
                var response = await client.GetAsync($"{BaseUrl}/domains?page=1&page_size=1");
                if (!response.IsSuccessStatusCode)
                    return ConnectionTestResult.Fail($"Linode returned {(int)response.StatusCode}.");
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
                int page = 1;
                while (true)
                {
                    var response = await client.GetAsync($"{BaseUrl}/domains?page={page}&page_size=100");
                    var body = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        _ = _logger.Debug($"Linode ListZones failed: {response.StatusCode}");
                        break;
                    }

                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    foreach (var d in root.GetProperty("data").EnumerateArray())
                    {
                        zones.Add(new ZoneInfo
                        {
                            Name = d.GetProperty("domain").GetString() ?? string.Empty,
                            ZoneId = d.GetProperty("id").GetInt32().ToString()
                        });
                    }

                    int totalPages = root.TryGetProperty("pages", out var tp) ? tp.GetInt32() : 1;
                    if (page >= totalPages)
                        break;
                    page++;
                }
            }
            catch (Exception ex)
            {
                _ = _logger.Debug($"Linode ListZones exception: {ex.Message}");
            }
            return zones;
        }

        public async Task<UpsertResult> UpsertTxtRecordAsync(string zone, string name, string value, int? ttlSeconds)
        {
            try
            {
                using var client = CreateClient();

                string? domainId = await GetDomainIdAsync(client, zone);
                if (string.IsNullOrEmpty(domainId))
                    return UpsertResult.Fail($"No Linode domain found for '{zone}'.");

                int ttl = Math.Max(ttlSeconds ?? 120, MinTtl);
                int? recordId = await GetTxtRecordIdAsync(client, domainId, name);

                var payload = JsonSerializer.Serialize(new
                {
                    type = "TXT",
                    name,
                    target = value,
                    ttl_sec = ttl
                });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                HttpResponseMessage response = recordId is null
                    ? await client.PostAsync($"{BaseUrl}/domains/{domainId}/records", content)
                    : await client.PutAsync($"{BaseUrl}/domains/{domainId}/records/{recordId}", content);

                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    _ = _logger.Info($"Linode TXT upserted for {name}.{zone}.");
                    return UpsertResult.Ok();
                }

                _ = _logger.Debug($"Linode upsert failed for {name}.{zone}: {response.StatusCode}\n{body}");
                return UpsertResult.Fail($"Linode returned {(int)response.StatusCode}.");
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

                string? domainId = await GetDomainIdAsync(client, zone);
                if (string.IsNullOrEmpty(domainId))
                    return DeleteResult.Fail($"No Linode domain found for '{zone}'.");

                int? recordId = await GetTxtRecordIdAsync(client, domainId, name);
                if (recordId is null)
                    return DeleteResult.Ok(); // Nothing to remove.

                var response = await client.DeleteAsync($"{BaseUrl}/domains/{domainId}/records/{recordId}");
                if (response.IsSuccessStatusCode)
                    return DeleteResult.Ok();

                var body = await response.Content.ReadAsStringAsync();
                _ = _logger.Debug($"Linode delete failed for {name}.{zone}: {response.StatusCode}\n{body}");
                return DeleteResult.Fail($"Linode returned {(int)response.StatusCode}.");
            }
            catch (Exception ex)
            {
                return DeleteResult.Fail(ex.Message);
            }
        }

        private static async Task<string?> GetDomainIdAsync(HttpClient client, string zone)
        {
            int page = 1;
            while (true)
            {
                var response = await client.GetAsync($"{BaseUrl}/domains?page={page}&page_size=100");
                if (!response.IsSuccessStatusCode)
                    return null;

                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                foreach (var d in root.GetProperty("data").EnumerateArray())
                {
                    if (string.Equals(d.GetProperty("domain").GetString(), zone, StringComparison.OrdinalIgnoreCase))
                        return d.GetProperty("id").GetInt32().ToString();
                }

                int totalPages = root.TryGetProperty("pages", out var tp) ? tp.GetInt32() : 1;
                if (page >= totalPages)
                    return null;
                page++;
            }
        }

        private static async Task<int?> GetTxtRecordIdAsync(HttpClient client, string domainId, string name)
        {
            int page = 1;
            while (true)
            {
                var response = await client.GetAsync($"{BaseUrl}/domains/{domainId}/records?page={page}&page_size=100");
                if (!response.IsSuccessStatusCode)
                    return null;

                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                foreach (var rec in root.GetProperty("data").EnumerateArray())
                {
                    if (rec.GetProperty("type").GetString() == "TXT" &&
                        string.Equals(rec.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return rec.GetProperty("id").GetInt32();
                    }
                }

                int totalPages = root.TryGetProperty("pages", out var tp) ? tp.GetInt32() : 1;
                if (page >= totalPages)
                    return null;
                page++;
            }
        }
    }
}
