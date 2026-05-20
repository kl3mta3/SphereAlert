using SphereAlert.Services.Config;

namespace SphereAlert.Services.Scripts
{
    /// <summary>
    /// Best-effort detection of whether sphere-alert.js is installed on a domain.
    /// Fetches https://&lt;domain&gt;/sphere-alert.js and checks for a 200 plus a
    /// recognizable signature. Detection never blocks: any failure yields "unknown".
    /// </summary>
    public class ScriptInstallDetector
    {
        // A distinctive string from the top of sphere-alert.js — survives minor edits.
        private const string Signature = "DNS-driven alert banner";

        private readonly Logger _logger;

        public ScriptInstallDetector(Logger logger)
        {
            _logger = logger;
        }

        /// <summary>Returns "installed", "missing", or "unknown".</summary>
        public async Task<string> DetectAsync(string domain)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SphereAlert-ScriptDetector/1.0");

                var response = await client.GetAsync($"https://{domain}/{ScriptService.FileName}");
                if (!response.IsSuccessStatusCode)
                    return "missing";

                var body = await response.Content.ReadAsStringAsync();
                return body.Contains(Signature, StringComparison.OrdinalIgnoreCase)
                    ? "installed"
                    : "missing";
            }
            catch (Exception ex)
            {
                _ = _logger.Debug($"Script detection for {domain} was inconclusive: {ex.Message}");
                return "unknown";
            }
        }
    }
}
