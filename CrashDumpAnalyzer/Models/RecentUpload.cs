namespace CrashDumpAnalyzer.Models
{
    public class RecentUpload
    {
        public int Id { get; init; }
        public DateTime UploadDate { get; init; }
        public string UploadedFromIp { get; init; } = string.Empty;
        public string UploadedFromHostname { get; set; } = string.Empty;
        public int DumpCallstackId { get; init; }
        public string CallStack { get; init; } = string.Empty;
        public string LogSummary { get; init; } = string.Empty;
        public bool IsDuplicate { get; init; }
    }
}