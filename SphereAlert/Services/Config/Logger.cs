namespace SphereAlert.Services.Config
{
    /// <summary>
    /// Lightweight append-only logger. Writes timestamped lines to the console and to
    /// a log file in the data volume. Registered as a singleton.
    /// </summary>
    public class Logger
    {
        private static readonly object _fileLock = new();

        public static string LogFilePath => ConfigureService.LogFilePath;

        public Task Info(string message) => Write("INFO", message);
        public Task Error(string message) => Write("ERROR", message);
        public Task Debug(string message) => Write("DEBUG", message);

        private static Task Write(string level, string message)
        {
            if (level == "DEBUG" && !ConfigureService.LogLevel.Equals("Debug", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
            try
            {
                Console.WriteLine(line);
                lock (_fileLock)
                {
                    File.AppendAllText(LogFilePath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never throw into the request path.
            }
            return Task.CompletedTask;
        }
    }
}
