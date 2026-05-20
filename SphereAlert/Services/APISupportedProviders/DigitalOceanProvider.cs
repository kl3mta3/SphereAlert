using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.APISupportedProviders
{
    /// <summary>
    /// DigitalOcean DNS provider. Credential format: a single Personal Access Token
    /// (Bearer token). Adapted from SphereSSL's DigitalOceanHelper.
    /// </summary>
    public class DigitalOceanProvider : IAlertDnsProvider
    {
        private const string BaseUrl = "https://api.digitalocean.com/v2";
        private const int MinTtl = 30;

        private readonly string _apiToken;
        private readonly Logger _logger;

        public DigitalOceanProvider(string credentials, Logger logger)
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
                var response = await client.GetAsync($"{BaseUrl}/account");
                if (!response.IsSuccessStatusCode)
                    return ConnectionTestResult.Fail($"DigitalOcean returned {(int)response.StatusCode}.");
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
                    var response = await client.GetAsync($"{BaseUrl}/domains?per_page=100&page={page}");
                    var body = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        _ = _logger.Debug($"DigitalOcean ListZones failed: {response.StatusCode}");
                        break;
                    }

                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    foreach (var domain in root.GetProperty("domains").EnumerateArray())
                    {
                        zones.Add(new ZoneInfo
                        {
                            Name = domain.GetProperty("name").GetString() ?? string.Empty,
                            ZoneId = domain.GetProperty("name").GetString()
                        });
                    }

                    if (root.TryGetProperty("links", out var links) &&
                        links.TryGetProperty("pages", out var pages) &&
                        pages.TryGetProperty("next", out _))
                    {
                        page++;
                        continue;
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                _ = _logger.Debug($"DigitalOcean ListZones exception: {ex.Message}");
            }
            return zones;
        }

        public async Task<UpsertResult> UpsertTxtRecordAsync(string zone, string name, string value, int? ttlSeconds)
        {
            try
            {
                using var client = CreateClient();

                int ttl = Math.Max(ttlSeconds ?? 120, MinTtl);
                int? recordId = await GetTxtRecordIdAsync(client, zone, name);

                var payload = JsonSerializer.Serialize(new
                {
                    type = "TXT",
                    name,
                    data = value,
                    ttl
                });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                HttpResponseMessage response = recordId is null
                    ? await client.PostAsync($"{BaseUrl}/domains/{zone}/records", content)
                    : await client.PutAsync($"{BaseUrl}/domains/{zone}/records/{recordId}", content);

                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    _ = _logger.Info($"DigitalOcean TXT upserted for {name}.{zone}.");
                    return UpsertResult.Ok();
                }

                _ = _logger.Debug($"DigitalOcean upsert failed for {name}.{zone}: {response.StatusCode}\n{body}");
                return UpsertResult.Fail($"DigitalOcean returned {(int)response.StatusCode}.");
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

                int? recordId = await GetTxtRecordIdAsync(client, zone, name);
                if (recordId is null)
                    return DeleteResult.Ok(); // Nothing to remove.

                var response = await client.DeleteAsync($"{BaseUrl}/domains/{zone}/records/{recordId}");
                if (response.IsSuccessStatusCode)
                    return DeleteResult.Ok();

                var body = await response.Content.ReadAsStringAsync();
                _ = _logger.Debug($"DigitalOcean delete failed for {name}.{zone}: {response.StatusCode}\n{body}");
                return DeleteResult.Fail($"DigitalOcean returned {(int)response.StatusCode}.");
            }
            catch (Exception ex)
            {
                return DeleteResult.Fail(ex.Message);
            }
        }

        private static async Task<int?> GetTxtRecordIdAsync(HttpClient client, string zone, string name)
        {
            int page = 1;
            while (true)
            {
                var response = await client.GetAsync($"{BaseUrl}/domains/{zone}/records?type=TXT&per_page=100&page={page}");
                if (!response.IsSuccessStatusCode)
                    return null;

                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                foreach (var record in root.GetProperty("domain_records").EnumerateArray())
                {
                    if (record.GetProperty("type").GetString() == "TXT" &&
                        string.Equals(record.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return record.GetProperty("id").GetInt32();
                    }
                }

                if (root.TryGetProperty("links", out var links) &&
                    links.TryGetProperty("pages", out var pages) &&
                    pages.TryGetProperty("next", out _))
                {
                    page++;
                    continue;
                }
                return null;
            }
        }
    }
}
