using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.APISupportedProviders
{
    /// <summary>
    /// Gandi LiveDNS provider. Credential format: a single Personal Access Token
    /// (Bearer) or a plain API key. Adapted from SphereSSL's GandiDNSHelper.
    /// </summary>
    public class GandiProvider : IAlertDnsProvider
    {
        private const string BaseUrl = "https://api.gandi.net/v5";
        private const int MinTtl = 300;

        private readonly string _credential;
        private readonly Logger _logger;

        public GandiProvider(string credentials, Logger logger)
        {
            _credential = credentials?.Trim() ?? string.Empty;
            _logger = logger;
        }

        private HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            // Personal Access Tokens use Bearer; legacy API keys use the Apikey scheme.
            // PATs are long (40+ chars) and typically prefixed; treat anything that
            // looks like a legacy key (no spaces, short-ish) the same way Gandi does.
            // Both schemes are accepted by setting Bearer first; if a plain key is
            // supplied callers should pass it as-is and Gandi's Apikey scheme is used.
            if (_credential.StartsWith("Apikey ", StringComparison.OrdinalIgnoreCase))
            {
                client.DefaultRequestHeaders.Add("Authorization", _credential);
            }
            else
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _credential);
            }
            return client;
        }

        private HttpClient CreateApikeyClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Apikey", _credential);
            return client;
        }

        public async Task<ConnectionTestResult> TestConnectionAsync()
        {
            try
            {
                using var client = CreateClient();
                var response = await client.GetAsync($"{BaseUrl}/domain/domains?per_page=1");
                if (response.IsSuccessStatusCode)
                    return ConnectionTestResult.Ok();

                // Fall back to the legacy Apikey scheme if Bearer is rejected.
                using var apikeyClient = CreateApikeyClient();
                var retry = await apikeyClient.GetAsync($"{BaseUrl}/domain/domains?per_page=1");
                if (retry.IsSuccessStatusCode)
                    return ConnectionTestResult.Ok();

                return ConnectionTestResult.Fail($"Gandi returned {(int)response.StatusCode}.");
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
                    var response = await client.GetAsync($"{BaseUrl}/domain/domains?per_page=100&page={page}");
                    var body = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        _ = _logger.Debug($"Gandi ListZones failed: {response.StatusCode}");
                        break;
                    }

                    using var doc = JsonDocument.Parse(body);
                    var array = doc.RootElement;
                    if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
                        break;

                    foreach (var domain in array.EnumerateArray())
                    {
                        string fqdn = domain.TryGetProperty("fqdn", out var f)
                            ? f.GetString() ?? string.Empty
                            : (domain.TryGetProperty("domain", out var d) ? d.GetString() ?? string.Empty : string.Empty);
                        zones.Add(new ZoneInfo { Name = fqdn, ZoneId = fqdn });
                    }
                    page++;
                }
            }
            catch (Exception ex)
            {
                _ = _logger.Debug($"Gandi ListZones exception: {ex.Message}");
            }
            return zones;
        }

        public async Task<UpsertResult> UpsertTxtRecordAsync(string zone, string name, string value, int? ttlSeconds)
        {
            try
            {
                using var client = CreateClient();

                int ttl = Math.Max(ttlSeconds ?? 120, MinTtl);

                // PUT on the record name/type replaces the rrset — a true upsert.
                var payload = JsonSerializer.Serialize(new
                {
                    rrset_ttl = ttl,
                    rrset_values = new[] { value }
                });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await client.PutAsync(
                    $"{BaseUrl}/livedns/domains/{zone}/records/{Uri.EscapeDataString(name)}/TXT", content);
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    _ = _logger.Info($"Gandi TXT upserted for {name}.{zone}.");
                    return UpsertResult.Ok();
                }

                _ = _logger.Debug($"Gandi upsert failed for {name}.{zone}: {response.StatusCode}\n{body}");
                return UpsertResult.Fail($"Gandi returned {(int)response.StatusCode}.");
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

                var response = await client.DeleteAsync(
                    $"{BaseUrl}/livedns/domains/{zone}/records/{Uri.EscapeDataString(name)}/TXT");

                if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return DeleteResult.Ok();

                var body = await response.Content.ReadAsStringAsync();
                _ = _logger.Debug($"Gandi delete failed for {name}.{zone}: {response.StatusCode}\n{body}");
                return DeleteResult.Fail($"Gandi returned {(int)response.StatusCode}.");
            }
            catch (Exception ex)
            {
                return DeleteResult.Fail(ex.Message);
            }
        }
    }
}
