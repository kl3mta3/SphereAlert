using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.APISupportedProviders
{
    /// <summary>
    /// Vultr DNS provider. Credential format: a single API key (Bearer token).
    /// Adapted from SphereSSL's VultrDNSHelper.
    /// </summary>
    public class VultrProvider : IAlertDnsProvider
    {
        private const string BaseUrl = "https://api.vultr.com/v2";
        private const int MinTtl = 120;

        private readonly string _apiToken;
        private readonly Logger _logger;

        public VultrProvider(string credentials, Logger logger)
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
                    return ConnectionTestResult.Fail($"Vultr returned {(int)response.StatusCode}.");
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
                string? cursor = null;
                while (true)
                {
                    string url = $"{BaseUrl}/domains?per_page=100";
                    if (!string.IsNullOrEmpty(cursor))
                        url += $"&cursor={Uri.EscapeDataString(cursor)}";

                    var response = await client.GetAsync(url);
                    var body = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        _ = _logger.Debug($"Vultr ListZones failed: {response.StatusCode}");
                        break;
                    }

                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    foreach (var domain in root.GetProperty("domains").EnumerateArray())
                    {
                        zones.Add(new ZoneInfo
                        {
                            Name = domain.GetProperty("domain").GetString() ?? string.Empty,
                            ZoneId = domain.GetProperty("domain").GetString()
                        });
                    }

                    if (root.TryGetProperty("meta", out var meta) &&
                        meta.TryGetProperty("links", out var links) &&
                        links.TryGetProperty("next", out var next))
                    {
                        cursor = next.GetString();
                        if (string.IsNullOrEmpty(cursor))
                            break;
                        continue;
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                _ = _logger.Debug($"Vultr ListZones exception: {ex.Message}");
            }
            return zones;
        }

        public async Task<UpsertResult> UpsertTxtRecordAsync(string zone, string name, string value, int? ttlSeconds)
        {
            try
            {
                using var client = CreateClient();

                int ttl = Math.Max(ttlSeconds ?? 120, MinTtl);
                string? recordId = await GetTxtRecordIdAsync(client, zone, name);

                if (recordId is null)
                {
                    var payload = JsonSerializer.Serialize(new
                    {
                        type = "TXT",
                        name,
                        data = value,
                        ttl
                    });
                    var response = await client.PostAsync($"{BaseUrl}/domains/{zone}/records",
                        new StringContent(payload, Encoding.UTF8, "application/json"));
                    var body = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        _ = _logger.Info($"Vultr TXT created for {name}.{zone}.");
                        return UpsertResult.Ok();
                    }
                    _ = _logger.Debug($"Vultr create failed for {name}.{zone}: {response.StatusCode}\n{body}");
                    return UpsertResult.Fail($"Vultr returned {(int)response.StatusCode}.");
                }
                else
                {
                    var payload = JsonSerializer.Serialize(new
                    {
                        name,
                        data = value,
                        ttl
                    });
                    var request = new HttpRequestMessage(HttpMethod.Patch,
                        $"{BaseUrl}/domains/{zone}/records/{recordId}")
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    };
                    var response = await client.SendAsync(request);
                    var body = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        _ = _logger.Info($"Vultr TXT updated for {name}.{zone}.");
                        return UpsertResult.Ok();
                    }
                    _ = _logger.Debug($"Vultr update failed for {name}.{zone}: {response.StatusCode}\n{body}");
                    return UpsertResult.Fail($"Vultr returned {(int)response.StatusCode}.");
                }
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

                string? recordId = await GetTxtRecordIdAsync(client, zone, name);
                if (recordId is null)
                    return DeleteResult.Ok(); // Nothing to remove.

                var response = await client.DeleteAsync($"{BaseUrl}/domains/{zone}/records/{recordId}");
                if (response.IsSuccessStatusCode)
                    return DeleteResult.Ok();

                var body = await response.Content.ReadAsStringAsync();
                _ = _logger.Debug($"Vultr delete failed for {name}.{zone}: {response.StatusCode}\n{body}");
                return DeleteResult.Fail($"Vultr returned {(int)response.StatusCode}.");
            }
            catch (Exception ex)
            {
                return DeleteResult.Fail(ex.Message);
            }
        }

        private static async Task<string?> GetTxtRecordIdAsync(HttpClient client, string zone, string name)
        {
            string? cursor = null;
            while (true)
            {
                string url = $"{BaseUrl}/domains/{zone}/records?per_page=100";
                if (!string.IsNullOrEmpty(cursor))
                    url += $"&cursor={Uri.EscapeDataString(cursor)}";

                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return null;

                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("records", out var records))
                {
                    foreach (var record in records.EnumerateArray())
                    {
                        if (record.GetProperty("type").GetString() == "TXT" &&
                            string.Equals(record.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
                        {
                            return record.GetProperty("id").GetString();
                        }
                    }
                }

                if (root.TryGetProperty("meta", out var meta) &&
                    meta.TryGetProperty("links", out var links) &&
                    links.TryGetProperty("next", out var next))
                {
                    cursor = next.GetString();
                    if (string.IsNullOrEmpty(cursor))
                        return null;
                    continue;
                }
                return null;
            }
        }
    }
}
