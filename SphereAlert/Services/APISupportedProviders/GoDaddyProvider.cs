using System.Text;
using System.Text.Json;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.APISupportedProviders
{
    /// <summary>
    /// GoDaddy DNS provider. Credential format: "ApiKey:ApiSecret", sent via the
    /// Authorization: sso-key header. Adapted from SphereSSL's GoDaddyDNSHelper.
    /// </summary>
    public class GoDaddyProvider : IAlertDnsProvider
    {
        private const string BaseUrl = "https://api.godaddy.com/v1";
        private const int MinTtl = 600;

        private readonly string _apiKey;
        private readonly string _apiSecret;
        private readonly Logger _logger;

        public GoDaddyProvider(string credentials, Logger logger)
        {
            var parts = (credentials ?? string.Empty).Split(':', 2);
            _apiKey = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            _apiSecret = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            _logger = logger;
        }

        private HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Add("Authorization", $"sso-key {_apiKey}:{_apiSecret}");
            return client;
        }

        public async Task<ConnectionTestResult> TestConnectionAsync()
        {
            try
            {
                using var client = CreateClient();
                var response = await client.GetAsync($"{BaseUrl}/domains?limit=1");
                if (!response.IsSuccessStatusCode)
                    return ConnectionTestResult.Fail($"GoDaddy returned {(int)response.StatusCode}.");
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
                var response = await client.GetAsync($"{BaseUrl}/domains?limit=1000");
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _ = _logger.Debug($"GoDaddy ListZones failed: {response.StatusCode}");
                    return zones;
                }

                using var doc = JsonDocument.Parse(body);
                foreach (var domain in doc.RootElement.EnumerateArray())
                {
                    string domainName = domain.GetProperty("domain").GetString() ?? string.Empty;
                    zones.Add(new ZoneInfo
                    {
                        Name = domainName,
                        ZoneId = domainName
                    });
                }
            }
            catch (Exception ex)
            {
                _ = _logger.Debug($"GoDaddy ListZones exception: {ex.Message}");
            }
            return zones;
        }

        public async Task<UpsertResult> UpsertTxtRecordAsync(string zone, string name, string value, int? ttlSeconds)
        {
            try
            {
                using var client = CreateClient();

                int ttl = Math.Max(ttlSeconds ?? 120, MinTtl);

                // GoDaddy expects an array of values for a TXT name; PUT replaces them.
                var payload = JsonSerializer.Serialize(new[]
                {
                    new { data = value, ttl }
                });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await client.PutAsync(
                    $"{BaseUrl}/domains/{zone}/records/TXT/{Uri.EscapeDataString(name)}", content);
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    _ = _logger.Info($"GoDaddy TXT upserted for {name}.{zone}.");
                    return UpsertResult.Ok();
                }

                _ = _logger.Debug($"GoDaddy upsert failed for {name}.{zone}: {response.StatusCode}\n{body}");
                return UpsertResult.Fail($"GoDaddy returned {(int)response.StatusCode}.");
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

                // GoDaddy has a DELETE for a specific record type/name.
                var response = await client.DeleteAsync(
                    $"{BaseUrl}/domains/{zone}/records/TXT/{Uri.EscapeDataString(name)}");

                if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return DeleteResult.Ok();

                var body = await response.Content.ReadAsStringAsync();
                _ = _logger.Debug($"GoDaddy delete failed for {name}.{zone}: {response.StatusCode}\n{body}");
                return DeleteResult.Fail($"GoDaddy returned {(int)response.StatusCode}.");
            }
            catch (Exception ex)
            {
                return DeleteResult.Fail(ex.Message);
            }
        }
    }
}
