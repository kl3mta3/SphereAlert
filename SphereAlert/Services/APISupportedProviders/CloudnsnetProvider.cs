using System.Text.Json;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.APISupportedProviders
{
    /// <summary>
    /// ClouDNS (cloudns.net) DNS provider. Credential format: "AuthId:AuthPassword".
    /// Adapted from SphereSSL's CloudnsnetDNSHelper.
    /// </summary>
    public class CloudnsnetProvider : IAlertDnsProvider
    {
        private const string BaseUrl = "https://api.cloudns.net";
        private const int MinTtl = 60;

        private readonly string _authId;
        private readonly string _authPassword;
        private readonly Logger _logger;

        public CloudnsnetProvider(string credentials, Logger logger)
        {
            var parts = (credentials ?? string.Empty).Split(':', 2);
            _authId = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            _authPassword = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            _logger = logger;
        }

        private static HttpClient CreateClient() =>
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>ClouDNS clamps TTL to a fixed set; pick the closest allowed value at or above the request.</summary>
        private static readonly int[] AllowedTtls =
            { 60, 300, 900, 1800, 3600, 21600, 43200, 86400, 172800, 259200, 604800, 1209600, 2592000 };

        private static int ClampTtl(int ttl)
        {
            foreach (int allowed in AllowedTtls)
                if (allowed >= ttl)
                    return allowed;
            return AllowedTtls[^1];
        }

        private FormUrlEncodedContent AuthForm(IEnumerable<KeyValuePair<string, string>>? extra = null)
        {
            var dict = new Dictionary<string, string>
            {
                ["auth-id"] = _authId,
                ["auth-password"] = _authPassword
            };
            if (extra is not null)
                foreach (var kvp in extra)
                    dict[kvp.Key] = kvp.Value;
            return new FormUrlEncodedContent(dict);
        }

        public async Task<ConnectionTestResult> TestConnectionAsync()
        {
            try
            {
                using var client = CreateClient();
                var response = await client.PostAsync(
                    $"{BaseUrl}/dns/list-zones.json",
                    AuthForm(new Dictionary<string, string> { ["page"] = "1", ["rows-per-page"] = "10" }));
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    return ConnectionTestResult.Fail($"ClouDNS returned {(int)response.StatusCode}.");

                // An auth failure returns a JSON object with status:Failed.
                if (body.Contains("\"status\":\"Failed\"", StringComparison.OrdinalIgnoreCase))
                    return ConnectionTestResult.Fail("ClouDNS rejected the credentials.");
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
                    var response = await client.PostAsync(
                        $"{BaseUrl}/dns/list-zones.json",
                        AuthForm(new Dictionary<string, string>
                        {
                            ["page"] = page.ToString(),
                            ["rows-per-page"] = "100"
                        }));
                    var body = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        _ = _logger.Debug($"ClouDNS ListZones failed: {response.StatusCode}");
                        break;
                    }

                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                        break;

                    foreach (var zone in root.EnumerateArray())
                    {
                        string zoneName = zone.GetProperty("name").GetString() ?? string.Empty;
                        zones.Add(new ZoneInfo { Name = zoneName, ZoneId = zoneName });
                    }

                    if (root.GetArrayLength() < 100)
                        break;
                    page++;
                }
            }
            catch (Exception ex)
            {
                _ = _logger.Debug($"ClouDNS ListZones exception: {ex.Message}");
            }
            return zones;
        }

        public async Task<UpsertResult> UpsertTxtRecordAsync(string zone, string name, string value, int? ttlSeconds)
        {
            try
            {
                using var client = CreateClient();

                int ttl = ClampTtl(Math.Max(ttlSeconds ?? 120, MinTtl));
                string? recordId = await GetTxtRecordIdAsync(client, zone, name);

                HttpResponseMessage response;
                if (string.IsNullOrEmpty(recordId))
                {
                    response = await client.PostAsync(
                        $"{BaseUrl}/dns/add-record.json",
                        AuthForm(new Dictionary<string, string>
                        {
                            ["domain-name"] = zone,
                            ["record-type"] = "TXT",
                            ["host"] = name,
                            ["record"] = value,
                            ["ttl"] = ttl.ToString()
                        }));
                }
                else
                {
                    response = await client.PostAsync(
                        $"{BaseUrl}/dns/mod-record.json",
                        AuthForm(new Dictionary<string, string>
                        {
                            ["domain-name"] = zone,
                            ["record-id"] = recordId,
                            ["record-type"] = "TXT",
                            ["host"] = name,
                            ["record"] = value,
                            ["ttl"] = ttl.ToString()
                        }));
                }

                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode &&
                    body.Contains("\"status\":\"Success\"", StringComparison.OrdinalIgnoreCase))
                {
                    _ = _logger.Info($"ClouDNS TXT upserted for {name}.{zone}.");
                    return UpsertResult.Ok();
                }

                _ = _logger.Debug($"ClouDNS upsert failed for {name}.{zone}: {response.StatusCode}\n{body}");
                return UpsertResult.Fail($"ClouDNS returned {(int)response.StatusCode}.");
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

                var response = await client.PostAsync(
                    $"{BaseUrl}/dns/delete-record.json",
                    AuthForm(new Dictionary<string, string>
                    {
                        ["domain-name"] = zone,
                        ["record-id"] = recordId
                    }));
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode &&
                    body.Contains("\"status\":\"Success\"", StringComparison.OrdinalIgnoreCase))
                    return DeleteResult.Ok();

                _ = _logger.Debug($"ClouDNS delete failed for {name}.{zone}: {response.StatusCode}\n{body}");
                return DeleteResult.Fail($"ClouDNS returned {(int)response.StatusCode}.");
            }
            catch (Exception ex)
            {
                return DeleteResult.Fail(ex.Message);
            }
        }

        private async Task<string?> GetTxtRecordIdAsync(HttpClient client, string zone, string name)
        {
            var response = await client.PostAsync(
                $"{BaseUrl}/dns/records.json",
                AuthForm(new Dictionary<string, string>
                {
                    ["domain-name"] = zone,
                    ["type"] = "TXT",
                    ["host"] = name
                }));
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync();
            // ClouDNS returns an object keyed by record id; an empty result is "[]".
            if (string.IsNullOrWhiteSpace(body) || body.TrimStart().StartsWith("["))
                return null;

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var record = prop.Value;
                if (record.ValueKind != JsonValueKind.Object)
                    continue;
                if (record.TryGetProperty("type", out var type) &&
                    type.GetString() == "TXT" &&
                    record.TryGetProperty("host", out var host) &&
                    string.Equals(host.GetString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return prop.Name;
                }
            }
            return null;
        }
    }
}
