using System.Text;
using System.Text.Json;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.APISupportedProviders
{
    /// <summary>
    /// Porkbun DNS provider. Credential format: "ApiKey:SecretApiKey", supplied in
    /// every JSON request body. Adapted from SphereSSL's PorkbunDNSHelper.
    /// </summary>
    public class PorkbunProvider : IAlertDnsProvider
    {
        private const string BaseUrl = "https://api.porkbun.com/api/json/v3";
        private const int MinTtl = 600;

        private readonly string _apiKey;
        private readonly string _secretApiKey;
        private readonly Logger _logger;

        public PorkbunProvider(string credentials, Logger logger)
        {
            var parts = (credentials ?? string.Empty).Split(':', 2);
            _apiKey = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            _secretApiKey = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            _logger = logger;
        }

        private static HttpClient CreateClient() =>
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private StringContent AuthBody(object? extra = null)
        {
            string json;
            if (extra is null)
            {
                json = JsonSerializer.Serialize(new
                {
                    apikey = _apiKey,
                    secretapikey = _secretApiKey
                });
            }
            else
            {
                // Merge auth fields with the supplied payload via a dictionary.
                var dict = new Dictionary<string, object?>
                {
                    ["apikey"] = _apiKey,
                    ["secretapikey"] = _secretApiKey
                };
                foreach (var prop in extra.GetType().GetProperties())
                    dict[prop.Name] = prop.GetValue(extra);
                json = JsonSerializer.Serialize(dict);
            }
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        public async Task<ConnectionTestResult> TestConnectionAsync()
        {
            try
            {
                using var client = CreateClient();
                var response = await client.PostAsync($"{BaseUrl}/ping", AuthBody());
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    return ConnectionTestResult.Fail($"Porkbun returned {(int)response.StatusCode}.");

                using var doc = JsonDocument.Parse(body);
                bool ok = doc.RootElement.TryGetProperty("status", out var status) &&
                          string.Equals(status.GetString(), "SUCCESS", StringComparison.OrdinalIgnoreCase);
                return ok
                    ? ConnectionTestResult.Ok()
                    : ConnectionTestResult.Fail("Porkbun rejected the API credentials.");
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
                var response = await client.PostAsync($"{BaseUrl}/domain/listAll", AuthBody());
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _ = _logger.Debug($"Porkbun ListZones failed: {response.StatusCode}");
                    return zones;
                }

                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("domains", out var domains))
                {
                    foreach (var domain in domains.EnumerateArray())
                    {
                        string domainName = domain.GetProperty("domain").GetString() ?? string.Empty;
                        zones.Add(new ZoneInfo { Name = domainName, ZoneId = domainName });
                    }
                }
            }
            catch (Exception ex)
            {
                _ = _logger.Debug($"Porkbun ListZones exception: {ex.Message}");
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

                HttpResponseMessage response;
                if (string.IsNullOrEmpty(recordId))
                {
                    response = await client.PostAsync($"{BaseUrl}/dns/create/{zone}", AuthBody(new
                    {
                        name,
                        type = "TXT",
                        content = value,
                        ttl = ttl.ToString()
                    }));
                }
                else
                {
                    response = await client.PostAsync($"{BaseUrl}/dns/edit/{zone}/{recordId}", AuthBody(new
                    {
                        name,
                        type = "TXT",
                        content = value,
                        ttl = ttl.ToString()
                    }));
                }

                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode && body.Contains("\"status\":\"SUCCESS\"", StringComparison.OrdinalIgnoreCase))
                {
                    _ = _logger.Info($"Porkbun TXT upserted for {name}.{zone}.");
                    return UpsertResult.Ok();
                }

                _ = _logger.Debug($"Porkbun upsert failed for {name}.{zone}: {response.StatusCode}\n{body}");
                return UpsertResult.Fail($"Porkbun returned {(int)response.StatusCode}.");
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
                if (string.IsNullOrEmpty(recordId))
                    return DeleteResult.Ok(); // Nothing to remove.

                var response = await client.PostAsync($"{BaseUrl}/dns/delete/{zone}/{recordId}", AuthBody());
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode && body.Contains("\"status\":\"SUCCESS\"", StringComparison.OrdinalIgnoreCase))
                    return DeleteResult.Ok();

                _ = _logger.Debug($"Porkbun delete failed for {name}.{zone}: {response.StatusCode}\n{body}");
                return DeleteResult.Fail($"Porkbun returned {(int)response.StatusCode}.");
            }
            catch (Exception ex)
            {
                return DeleteResult.Fail(ex.Message);
            }
        }

        private async Task<string?> GetTxtRecordIdAsync(HttpClient client, string zone, string name)
        {
            var response = await client.PostAsync($"{BaseUrl}/dns/retrieve/{zone}", AuthBody());
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("records", out var records))
            {
                // Porkbun returns the record "name" as the full FQDN.
                string fqdn = $"{name}.{zone}";
                foreach (var record in records.EnumerateArray())
                {
                    string recName = record.GetProperty("name").GetString() ?? string.Empty;
                    if (record.GetProperty("type").GetString() == "TXT" &&
                        (string.Equals(recName, fqdn, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(recName, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        return record.GetProperty("id").GetString();
                    }
                }
            }
            return null;
        }
    }
}
