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

        // Where the script may live. The documented location is /js/sphere-alert.js;
        // the root path is checked as a fallback for older installs.
        private static readonly string[] CandidatePaths = { "js/sphere-alert.js", "sphere-alert.js" };

        /// <summary>Returns "installed", "missing", or "unknown".</summary>
        public async Task<string> DetectAsync(string domain)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SphereAlert-ScriptDetector/1.0");

            bool siteResponded = false;

            foreach (var path in CandidatePaths)
            {
                try
                {
                    var response = await client.GetAsync($"https://{domain}/{path}");
                    siteResponded = true; // The server answered — even a 404 counts.

                    if (response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        if (body.Contains(Signature, StringComparison.OrdinalIgnoreCase))
                            return "installed";
                    }
                }
                catch (Exception ex)
                {
                    _ = _logger.Debug($"Script detection for {domain}/{path} was inconclusive: {ex.Message}");
                }
            }

            // The site answered but the script was not found anywhere → missing.
            // The site could not be reached at all → unknown (never block).
            return siteResponded ? "missing" : "unknown";
        }
    }
}
