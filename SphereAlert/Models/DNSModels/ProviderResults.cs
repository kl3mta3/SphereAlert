namespace SphereAlert.Models.DNSModels
{
    /// <summary>Result of writing/upserting a TXT record.</summary>
    public class UpsertResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }

        public static UpsertResult Ok() => new() { Success = true };
        public static UpsertResult Fail(string error) => new() { Success = false, Error = error };
    }

    /// <summary>Result of deleting a TXT record.</summary>
    public class DeleteResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }

        public static DeleteResult Ok() => new() { Success = true };
        public static DeleteResult Fail(string error) => new() { Success = false, Error = error };
    }

    /// <summary>Result of a lightweight credential / connectivity check.</summary>
    public class ConnectionTestResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }

        public static ConnectionTestResult Ok() => new() { Success = true };
        public static ConnectionTestResult Fail(string error) => new() { Success = false, Error = error };
    }

    /// <summary>A DNS zone reported by a provider, used for domain import.</summary>
    public class ZoneInfo
    {
        public string Name { get; set; } = string.Empty;
        public string? ZoneId { get; set; }
    }
}
