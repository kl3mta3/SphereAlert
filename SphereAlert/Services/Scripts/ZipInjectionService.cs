using System.IO.Compression;

namespace SphereAlert.Services.Scripts
{
    public class ZipInjectionResult
    {
        public byte[] ZipBytes { get; set; } = Array.Empty<byte>();
        public int HtmlFilesScanned { get; set; }
        public int HtmlFilesInjected { get; set; }
        public int HtmlFilesAlreadyTagged { get; set; }
    }

    /// <summary>
    /// Takes an uploaded site archive, injects the sphere-alert.js script tag into
    /// every HTML file that lacks it, adds sphere-alert.js to the archive root, and
    /// repackages it for download. No FTP/SSH — the operator re-uploads the result.
    /// </summary>
    public class ZipInjectionService
    {
        /// <summary>Where the script is placed inside the archive (the js/ folder is created if absent).</summary>
        private const string ScriptZipPath = "js/sphere-alert.js";
        private const string ScriptTag = "<script src=\"/js/sphere-alert.js\" defer></script>";

        private readonly ScriptService _scriptService;

        public ZipInjectionService(ScriptService scriptService)
        {
            _scriptService = scriptService;
        }

        public async Task<ZipInjectionResult> ProcessAsync(Stream uploadedZip)
        {
            var result = new ZipInjectionResult();

            var buffer = new MemoryStream();
            await uploadedZip.CopyToAsync(buffer);
            buffer.Position = 0;

            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
            {
                var htmlEntries = archive.Entries
                    .Where(e => e.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                             || e.Name.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var rewrites = new List<(string fullName, string content)>();
                foreach (var entry in htmlEntries)
                {
                    result.HtmlFilesScanned++;

                    string content;
                    using (var reader = new StreamReader(entry.Open()))
                        content = await reader.ReadToEndAsync();

                    if (content.Contains("sphere-alert.js", StringComparison.OrdinalIgnoreCase))
                    {
                        result.HtmlFilesAlreadyTagged++;
                        continue;
                    }

                    rewrites.Add((entry.FullName, InjectTag(content)));
                }

                foreach (var (fullName, content) in rewrites)
                {
                    archive.GetEntry(fullName)?.Delete();
                    var newEntry = archive.CreateEntry(fullName, CompressionLevel.Optimal);
                    using var writer = new StreamWriter(newEntry.Open());
                    await writer.WriteAsync(content);
                    result.HtmlFilesInjected++;
                }

                // Place the script in a js/ folder, creating it if the archive lacks one.
                if (archive.GetEntry(ScriptZipPath) == null)
                {
                    var scriptEntry = archive.CreateEntry(ScriptZipPath, CompressionLevel.Optimal);
                    using var writer = new StreamWriter(scriptEntry.Open());
                    await writer.WriteAsync(_scriptService.Content);
                }
            }

            result.ZipBytes = buffer.ToArray();
            return result;
        }

        /// <summary>Inserts the script tag right after &lt;head&gt;, or &lt;body&gt;, or at the top.</summary>
        private static string InjectTag(string html)
        {
            string injection = "\n    " + ScriptTag;

            int afterHead = AfterOpeningTag(html, "<head");
            if (afterHead >= 0)
                return html.Insert(afterHead, injection);

            int afterBody = AfterOpeningTag(html, "<body");
            if (afterBody >= 0)
                return html.Insert(afterBody, injection);

            return ScriptTag + "\n" + html;
        }

        /// <summary>Returns the index just past the closing '>' of an opening tag, or -1.</summary>
        private static int AfterOpeningTag(string html, string tagStart)
        {
            int start = html.IndexOf(tagStart, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return -1;
            int close = html.IndexOf('>', start);
            return close < 0 ? -1 : close + 1;
        }
    }
}
