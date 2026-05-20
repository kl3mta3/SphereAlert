using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.APISupportedProviders
{
    /// <summary>
    /// DNS Made Easy provider. Credential format: "ApiKey:SecretKey". Every request
    /// is signed with an HMAC-SHA1 of the request date. Adapted from SphereSSL's
    /// DNSMadeEasyDNSHelper.
    /// </summary>
    public class DNSMadeEasyProvider : IAlertDnsProvider
    {
        private const string BaseUrl = "https://api.dnsmadeeasy.com/V2.0";
        private const int MinTtl = 30;

        private readonly string _apiKey;
        private readonly string _secretKey;
        private readonly Logger _logger;

        public DNSMadeEasyProvider(string credentials, Logger logger)
        {
            var parts = (credentials ?? string.Empty).Split(':', 2);
            _apiKey = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            _secretKey = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            _logger = logger;
        }

        private static HttpClient CreateClient() =>
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>
        /// Applies the three DNS Made Easy auth headers. DNSME signs the HMAC over
        /// the request date string only (RFC1123 UTC).
        /// </summary>
        private void ApplyAuthHeaders(HttpRequestMessage request)
        {
            string requestDate = DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);
            string hmac = ComputeHmac(_secretKey, requestDate);
            request.Headers.Add("x-dnsme-apiKey", _apiKey);
            request.Headers.Add("x-dnsme-requestDate", requestDate);
            request.Headers.Add("x-dnsme-hmac", hmac);
        }

        private async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string? jsonBody = null)
        {
            var request = new HttpRequestMessage(method, url);
            ApplyAuthHeaders(request);
            if (jsonBody is not null)
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            return await client.SendAsync(request);
        }

        public async Task<ConnectionTestResult> TestConnectionAsync()
        {
            try
            {
                using var client = CreateClient();
                var response = await SendAsync(client, HttpMethod.Get, $"{BaseUrl}/dns/managed");
                if (!response.IsSuccessStatusCode)
                    return ConnectionTestResult.Fail($"DNS Made Easy returned {(int)response.StatusCode}.");
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
                var response = await SendAsync(client, HttpMethod.Get, $"{BaseUrl}/dns/managed");
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _ = _logger.Debug($"DNS Made Easy ListZones failed: {response.StatusCode}");
                    return zones;
                }

                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data))
                {
                    foreach (var entry in data.EnumerateArray())
                    {
                        zones.Add(new ZoneInfo
                        {
                            Name = entry.GetProperty("name").GetString() ?? string.Empty,
                            ZoneId = entry.GetProperty("id").GetInt32().ToString()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _ = _logger.Debug($"DNS Made Easy ListZones exception: {ex.Message}");
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
                    return UpsertResult.Fail($"No DNS Made Easy zone found for '{zone}'.");

                int ttl = Math.Max(ttlSeconds ?? 120, MinTtl);
                int? recordId = await GetTxtRecordIdAsync(client, zoneId, name);

                if (recordId is null)
                {
                    var payload = JsonSerializer.Serialize(new
                    {
                        type = "TXT",
                        name,
                        value,
                        ttl
                    });
                    var response = await SendAsync(client, HttpMethod.Post,
                        $"{BaseUrl}/dns/managed/{zoneId}/records", payload);
                    var body = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        _ = _logger.Info($"DNS Made Easy TXT created for {name}.{zone}.");
                        return UpsertResult.Ok();
                    }
                    _ = _logger.Debug($"DNS Made Easy create failed for {name}.{zone}: {response.StatusCode}\n{body}");
                    return UpsertResult.Fail($"DNS Made Easy returned {(int)response.StatusCode}.");
                }
                else
                {
                    // PUT to update an existing record; DNSME requires the id in the body.
                    var payload = JsonSerializer.Serialize(new
                    {
                        id = recordId,
                        type = "TXT",
                        name,
                        value,
                        ttl
                    });
                    var response = await SendAsync(client, HttpMethod.Put,
                        $"{BaseUrl}/dns/managed/{zoneId}/records/{recordId}", payload);
                    var body = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        _ = _logger.Info($"DNS Made Easy TXT updated for {name}.{zone}.");
                        return UpsertResult.Ok();
                    }
                    _ = _logger.Debug($"DNS Made Easy update failed for {name}.{zone}: {response.StatusCode}\n{body}");
                    return UpsertResult.Fail($"DNS Made Easy returned {(int)response.StatusCode}.");
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

                string? zoneId = await GetZoneIdAsync(client, zone);
                if (string.IsNullOrEmpty(zoneId))
                    return DeleteResult.Fail($"No DNS Made Easy zone found for '{zone}'.");

                int? recordId = await GetTxtRecordIdAsync(client, zoneId, name);
                if (recordId is null)
                    return DeleteResult.Ok(); // Nothing to remove.

                var response = await SendAsync(client, HttpMethod.Delete,
                    $"{BaseUrl}/dns/managed/{zoneId}/records/{recordId}");
                if (response.IsSuccessStatusCode)
                    return DeleteResult.Ok();

                var body = await response.Content.ReadAsStringAsync();
                _ = _logger.Debug($"DNS Made Easy delete failed for {name}.{zone}: {response.StatusCode}\n{body}");
                return DeleteResult.Fail($"DNS Made Easy returned {(int)response.StatusCode}.");
            }
            catch (Exception ex)
            {
                return DeleteResult.Fail(ex.Message);
            }
        }

        private async Task<string?> GetZoneIdAsync(HttpClient client, string zone)
        {
            var response = await SendAsync(client, HttpMethod.Get, $"{BaseUrl}/dns/managed");
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var entry in data.EnumerateArray())
                {
                    if (string.Equals(entry.GetProperty("name").GetString(), zone, StringComparison.OrdinalIgnoreCase))
                        return entry.GetProperty("id").GetInt32().ToString();
                }
            }
            return null;
        }

        private async Task<int?> GetTxtRecordIdAsync(HttpClient client, string zoneId, string name)
        {
            var response = await SendAsync(client, HttpMethod.Get,
                $"{BaseUrl}/dns/managed/{zoneId}/records?type=TXT");
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var rec in data.EnumerateArray())
                {
                    if (rec.GetProperty("type").GetString() == "TXT" &&
                        string.Equals(rec.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return rec.GetProperty("id").GetInt32();
                    }
                }
            }
            return null;
        }

        private static string ComputeHmac(string secretKey, string requestDate)
        {
            using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secretKey));
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(requestDate));
            // DNS Made Easy expects a lowercase hex digest.
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
