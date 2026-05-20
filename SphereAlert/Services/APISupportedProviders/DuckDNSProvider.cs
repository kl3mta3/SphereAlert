using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.APISupportedProviders
{
    /// <summary>
    /// DuckDNS provider. Credential format: a single token. Adapted from SphereSSL's
    /// DuckDNSHelper.
    ///
    /// TODO: DuckDNS is severely limited. It supports only ONE TXT value per duckdns
    /// subdomain, set via the update API — there is no arbitrary "alert." subdomain
    /// and no zone enumeration. The "name" argument is therefore ignored; the TXT is
    /// always written at the duckdns subdomain itself. ListZonesAsync returns an empty
    /// list because DuckDNS cannot enumerate domains.
    /// </summary>
    public class DuckDNSProvider : IAlertDnsProvider
    {
        private const string BaseUrl = "https://www.duckdns.org/update";

        private readonly string _token;
        private readonly Logger _logger;

        public DuckDNSProvider(string credentials, Logger logger)
        {
            _token = credentials?.Trim() ?? string.Empty;
            _logger = logger;
        }

        private static HttpClient CreateClient() =>
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>
        /// Extracts the bare duckdns subdomain label from a zone string such as
        /// "myname.duckdns.org" or "myname".
        /// </summary>
        private static string ExtractSubdomain(string zone)
        {
            string z = (zone ?? string.Empty).Trim().TrimEnd('.');
            const string suffix = ".duckdns.org";
            if (z.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                z = z.Substring(0, z.Length - suffix.Length);
            // If a deeper label was supplied (e.g. "alert.myname"), keep the last label.
            int dot = z.LastIndexOf('.');
            return dot >= 0 ? z.Substring(dot + 1) : z;
        }

        public async Task<ConnectionTestResult> TestConnectionAsync()
        {
            try
            {
                using var client = CreateClient();
                // A harmless update call with no IP change still returns "OK" for a valid token.
                var response = await client.GetAsync(
                    $"{BaseUrl}?domains=&token={Uri.EscapeDataString(_token)}");
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    return ConnectionTestResult.Fail($"DuckDNS returned {(int)response.StatusCode}.");

                // DuckDNS replies "OK" or "KO". An empty domains list with a valid
                // token still yields "OK".
                if (body.Trim().StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                    return ConnectionTestResult.Ok();
                return ConnectionTestResult.Fail("DuckDNS rejected the token.");
            }
            catch (Exception ex)
            {
                return ConnectionTestResult.Fail(ex.Message);
            }
        }

        public Task<List<ZoneInfo>> ListZonesAsync()
        {
            // TODO: DuckDNS has no domain enumeration API; nothing to return.
            return Task.FromResult(new List<ZoneInfo>());
        }

        public async Task<UpsertResult> UpsertTxtRecordAsync(string zone, string name, string value, int? ttlSeconds)
        {
            try
            {
                // DuckDNS ignores TTL and the record name; the TXT lives on the
                // duckdns subdomain itself.
                _ = ttlSeconds;
                _ = name;

                string subdomain = ExtractSubdomain(zone);
                if (string.IsNullOrEmpty(subdomain))
                    return UpsertResult.Fail("Could not determine DuckDNS subdomain from zone.");

                using var client = CreateClient();
                var url = $"{BaseUrl}?domains={Uri.EscapeDataString(subdomain)}" +
                          $"&token={Uri.EscapeDataString(_token)}" +
                          $"&txt={Uri.EscapeDataString(value)}&clear=false";
                var response = await client.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode &&
                    body.Trim().StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                {
                    _ = _logger.Info($"DuckDNS TXT set for {subdomain}.duckdns.org.");
                    return UpsertResult.Ok();
                }

                _ = _logger.Debug($"DuckDNS upsert failed for {subdomain}: {response.StatusCode}\n{body}");
                return UpsertResult.Fail($"DuckDNS update failed (response: {body.Trim()}).");
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
                _ = name;

                string subdomain = ExtractSubdomain(zone);
                if (string.IsNullOrEmpty(subdomain))
                    return DeleteResult.Fail("Could not determine DuckDNS subdomain from zone.");

                using var client = CreateClient();
                var url = $"{BaseUrl}?domains={Uri.EscapeDataString(subdomain)}" +
                          $"&token={Uri.EscapeDataString(_token)}&txt=&clear=true";
                var response = await client.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode &&
                    body.Trim().StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                {
                    _ = _logger.Info($"DuckDNS TXT cleared for {subdomain}.duckdns.org.");
                    return DeleteResult.Ok();
                }

                _ = _logger.Debug($"DuckDNS delete failed for {subdomain}: {response.StatusCode}\n{body}");
                return DeleteResult.Fail($"DuckDNS clear failed (response: {body.Trim()}).");
            }
            catch (Exception ex)
            {
                return DeleteResult.Fail(ex.Message);
            }
        }
    }
}
