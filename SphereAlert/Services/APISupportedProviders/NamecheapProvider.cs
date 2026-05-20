using System.Text;
using System.Xml.Linq;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.APISupportedProviders
{
    /// <summary>
    /// Namecheap DNS provider. Credential format: "ApiUser:ApiKey:ClientIp".
    /// Adapted from SphereSSL's NamecheapDNSHelper.
    ///
    /// TODO: Namecheap's DNS API has no per-record operations — the only way to
    /// change DNS is namecheap.domains.dns.setHosts, which REPLACES the entire host
    /// record set for a domain. Upsert/Delete therefore read the full host list via
    /// getHosts, modify the "alert" TXT entry, and re-submit every record. This is
    /// best-effort: the getHosts response is parsed for the documented host
    /// attributes, but any record type Namecheap does not echo back verbatim (e.g.
    /// some exotic flags) could be dropped on rewrite. The ApiUser value is reused as
    /// the required UserName field, which is correct for the common single-account
    /// case.
    /// </summary>
    public class NamecheapProvider : IAlertDnsProvider
    {
        private const string BaseUrl = "https://api.namecheap.com/xml.response";
        private const int MinTtl = 60;

        private static readonly XNamespace Ns = "http://api.namecheap.com/xml.response";

        private readonly string _apiUser;
        private readonly string _apiKey;
        private readonly string _clientIp;
        private readonly Logger _logger;

        public NamecheapProvider(string credentials, Logger logger)
        {
            var parts = (credentials ?? string.Empty).Split(':');
            _apiUser = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            _apiKey = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            _clientIp = parts.Length > 2 ? parts[2].Trim() : string.Empty;
            _logger = logger;
        }

        private static HttpClient CreateClient() =>
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private string CommonQuery(string command) =>
            $"ApiUser={Uri.EscapeDataString(_apiUser)}" +
            $"&ApiKey={Uri.EscapeDataString(_apiKey)}" +
            $"&UserName={Uri.EscapeDataString(_apiUser)}" +
            $"&ClientIp={Uri.EscapeDataString(_clientIp)}" +
            $"&Command={command}";

        private static (string sld, string tld) SplitDomain(string zone)
        {
            string z = (zone ?? string.Empty).Trim().TrimEnd('.');
            var parts = z.Split('.');
            if (parts.Length < 2)
                return (z, string.Empty);
            // Namecheap treats everything before the last label as the SLD.
            string tld = parts[^1];
            string sld = string.Join('.', parts[..^1]);
            return (sld, tld);
        }

        public async Task<ConnectionTestResult> TestConnectionAsync()
        {
            try
            {
                using var client = CreateClient();
                var url = $"{BaseUrl}?{CommonQuery("namecheap.domains.getList")}&PageSize=2";
                var response = await client.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    return ConnectionTestResult.Fail($"Namecheap returned {(int)response.StatusCode}.");

                var doc = XDocument.Parse(body);
                string? status = doc.Root?.Attribute("Status")?.Value;
                if (string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
                    return ConnectionTestResult.Ok();

                return ConnectionTestResult.Fail($"Namecheap rejected the credentials: {ExtractError(doc)}");
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
                    var url = $"{BaseUrl}?{CommonQuery("namecheap.domains.getList")}&Page={page}&PageSize=100";
                    var response = await client.GetAsync(url);
                    var body = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        _ = _logger.Debug($"Namecheap ListZones failed: {response.StatusCode}");
                        break;
                    }

                    var doc = XDocument.Parse(body);
                    if (!string.Equals(doc.Root?.Attribute("Status")?.Value, "OK", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = _logger.Debug($"Namecheap ListZones error: {ExtractError(doc)}");
                        break;
                    }

                    var domainElements = doc.Descendants(Ns + "Domain").ToList();
                    if (domainElements.Count == 0)
                        break;

                    foreach (var domain in domainElements)
                    {
                        string domainName = domain.Attribute("Name")?.Value ?? string.Empty;
                        if (!string.IsNullOrEmpty(domainName))
                            zones.Add(new ZoneInfo { Name = domainName, ZoneId = domainName });
                    }

                    if (domainElements.Count < 100)
                        break;
                    page++;
                }
            }
            catch (Exception ex)
            {
                _ = _logger.Debug($"Namecheap ListZones exception: {ex.Message}");
            }
            return zones;
        }

        public async Task<UpsertResult> UpsertTxtRecordAsync(string zone, string name, string value, int? ttlSeconds)
        {
            try
            {
                using var client = CreateClient();
                var (sld, tld) = SplitDomain(zone);
                int ttl = Math.Max(ttlSeconds ?? 120, MinTtl);

                // Read the full host list so we can re-submit it intact.
                var hosts = await GetHostsAsync(client, sld, tld);
                if (hosts is null)
                    return UpsertResult.Fail("Namecheap getHosts failed; cannot safely rewrite hosts.");

                // Replace or add the alert TXT record.
                hosts.RemoveAll(h =>
                    string.Equals(h.Type, "TXT", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(h.HostName, name, StringComparison.OrdinalIgnoreCase));
                hosts.Add(new HostRecord
                {
                    HostName = name,
                    Type = "TXT",
                    Address = value,
                    Ttl = ttl.ToString(),
                    MxPref = "10"
                });

                bool ok = await SetHostsAsync(client, sld, tld, hosts);
                if (ok)
                {
                    _ = _logger.Info($"Namecheap TXT upserted for {name}.{zone}.");
                    return UpsertResult.Ok();
                }
                return UpsertResult.Fail("Namecheap setHosts failed.");
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
                var (sld, tld) = SplitDomain(zone);

                var hosts = await GetHostsAsync(client, sld, tld);
                if (hosts is null)
                    return DeleteResult.Fail("Namecheap getHosts failed; cannot safely rewrite hosts.");

                int removed = hosts.RemoveAll(h =>
                    string.Equals(h.Type, "TXT", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(h.HostName, name, StringComparison.OrdinalIgnoreCase));

                if (removed == 0)
                    return DeleteResult.Ok(); // Nothing to remove.

                bool ok = await SetHostsAsync(client, sld, tld, hosts);
                return ok
                    ? DeleteResult.Ok()
                    : DeleteResult.Fail("Namecheap setHosts failed.");
            }
            catch (Exception ex)
            {
                return DeleteResult.Fail(ex.Message);
            }
        }

        private sealed class HostRecord
        {
            public string HostName { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string Address { get; set; } = string.Empty;
            public string Ttl { get; set; } = "1800";
            public string MxPref { get; set; } = "10";
        }

        private async Task<List<HostRecord>?> GetHostsAsync(HttpClient client, string sld, string tld)
        {
            var url = $"{BaseUrl}?{CommonQuery("namecheap.domains.dns.getHosts")}" +
                      $"&SLD={Uri.EscapeDataString(sld)}&TLD={Uri.EscapeDataString(tld)}";
            var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return null;

            var doc = XDocument.Parse(body);
            if (!string.Equals(doc.Root?.Attribute("Status")?.Value, "OK", StringComparison.OrdinalIgnoreCase))
            {
                _ = _logger.Debug($"Namecheap getHosts error: {ExtractError(doc)}");
                return null;
            }

            var hosts = new List<HostRecord>();
            foreach (var host in doc.Descendants(Ns + "host"))
            {
                hosts.Add(new HostRecord
                {
                    HostName = host.Attribute("Name")?.Value ?? "@",
                    Type = host.Attribute("Type")?.Value ?? string.Empty,
                    Address = host.Attribute("Address")?.Value ?? string.Empty,
                    Ttl = host.Attribute("TTL")?.Value ?? "1800",
                    MxPref = host.Attribute("MXPref")?.Value ?? "10"
                });
            }
            return hosts;
        }

        private async Task<bool> SetHostsAsync(HttpClient client, string sld, string tld, List<HostRecord> hosts)
        {
            var sb = new StringBuilder();
            sb.Append($"{BaseUrl}?{CommonQuery("namecheap.domains.dns.setHosts")}");
            sb.Append($"&SLD={Uri.EscapeDataString(sld)}&TLD={Uri.EscapeDataString(tld)}");

            for (int i = 0; i < hosts.Count; i++)
            {
                int n = i + 1;
                var h = hosts[i];
                sb.Append($"&HostName{n}={Uri.EscapeDataString(string.IsNullOrEmpty(h.HostName) ? "@" : h.HostName)}");
                sb.Append($"&RecordType{n}={Uri.EscapeDataString(h.Type)}");
                sb.Append($"&Address{n}={Uri.EscapeDataString(h.Address)}");
                sb.Append($"&TTL{n}={Uri.EscapeDataString(h.Ttl)}");
                if (string.Equals(h.Type, "MX", StringComparison.OrdinalIgnoreCase))
                    sb.Append($"&MXPref{n}={Uri.EscapeDataString(h.MxPref)}");
            }

            var response = await client.GetAsync(sb.ToString());
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _ = _logger.Debug($"Namecheap setHosts failed: {response.StatusCode}\n{body}");
                return false;
            }

            var doc = XDocument.Parse(body);
            if (string.Equals(doc.Root?.Attribute("Status")?.Value, "OK", StringComparison.OrdinalIgnoreCase))
                return true;

            _ = _logger.Debug($"Namecheap setHosts error: {ExtractError(doc)}");
            return false;
        }

        private static string ExtractError(XDocument doc)
        {
            var error = doc.Descendants(Ns + "Error").FirstOrDefault();
            return error?.Value ?? "unknown error";
        }
    }
}
