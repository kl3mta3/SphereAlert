using System.Text.RegularExpressions;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.Scripts
{
    /// <summary>
    /// Best-effort detection of whether sphere-alert.js is installed on a domain.
    /// Two install styles are recognized:
    ///   1. Self-hosted — the file served from &lt;domain&gt;/js/sphere-alert.js.
    ///   2. CDN embed   — a &lt;script&gt; tag on the homepage whose src points at
    ///      sphere-alert.js on any host (e.g. jsDelivr).
    /// Detection never blocks: any failure yields "unknown".
    /// </summary>
    public class ScriptInstallDetector
    {
        // A distinctive string from the top of sphere-alert.js — survives minor edits.
        private const string Signature = "DNS-driven alert banner";

        // Matches a <script ... src="...sphere-alert.js..."> tag in page HTML,
        // regardless of which host serves the file.
        private static readonly Regex ScriptTagPattern = new(
            "<script\\b[^>]*\\bsrc\\s*=\\s*[\"'][^\"']*sphere-alert\\.js[^\"']*[\"']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly Logger _logger;

        public ScriptInstallDetector(Logger logger)
        {
            _logger = logger;
        }

        // Where the file may be self-hosted. The documented location is
        // /js/sphere-alert.js; the root path is a fallback for older installs.
        private static readonly string[] CandidatePaths = { "js/sphere-alert.js", "sphere-alert.js" };

        /// <summary>Returns "installed", "missing", or "unknown".</summary>
        public async Task<string> DetectAsync(string domain)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SphereAlert-ScriptDetector/1.0");

            bool siteResponded = false;

            // 1. Self-hosted: the script file served straight from the domain.
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

            // 2. CDN embed: a <script src="...sphere-alert.js"> tag in the homepage.
            try
            {
                var response = await client.GetAsync($"https://{domain}/");
                siteResponded = true;

                if (response.IsSuccessStatusCode)
                {
                    var html = await response.Content.ReadAsStringAsync();
                    if (ScriptTagPattern.IsMatch(html))
                        return "installed";
                }
            }
            catch (Exception ex)
            {
                _ = _logger.Debug($"Script detection for {domain} homepage was inconclusive: {ex.Message}");
            }

            // The site answered but the script was not found anywhere → missing.
            // The site could not be reached at all → unknown (never block).
            return siteResponded ? "missing" : "unknown";
        }
    }
}
