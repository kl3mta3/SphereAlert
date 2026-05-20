using SphereAlert.Models.DNSModels;

namespace SphereAlert.Services.APISupportedProviders
{
    /// <summary>
    /// The DNS operations SphereAlert needs from every provider. Adapted from
    /// SphereSSL's DNS-01 challenge writers — instead of writing to
    /// _acme-challenge.&lt;domain&gt; with ACME tokens, these write arbitrary string
    /// values to alert.&lt;domain&gt;.
    ///
    /// The fully-qualified record name is always "&lt;name&gt;.&lt;zone&gt;"
    /// (e.g. name "alert" + zone "example.com" → "alert.example.com").
    /// </summary>
    public interface IAlertDnsProvider
    {
        /// <summary>Creates or replaces the TXT record at &lt;name&gt;.&lt;zone&gt;.</summary>
        Task<UpsertResult> UpsertTxtRecordAsync(string zone, string name, string value, int? ttlSeconds);

        /// <summary>Removes the TXT record at &lt;name&gt;.&lt;zone&gt;.</summary>
        Task<DeleteResult> DeleteTxtRecordAsync(string zone, string name);

        /// <summary>Lists the DNS zones this credential set can manage — used for domain import.</summary>
        Task<List<ZoneInfo>> ListZonesAsync();

        /// <summary>Lightweight credential / connectivity check for the "Test connection" button.</summary>
        Task<ConnectionTestResult> TestConnectionAsync();
    }
}
