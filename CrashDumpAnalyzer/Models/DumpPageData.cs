using CrashDumpAnalyzer.IssueTrackers.Data;

namespace CrashDumpAnalyzer.Models
{
    public class DumpPageData
    {
        public required CrashDumpAnalyzer.Models.DumpCallstack Callstack { get; set; }
        public Dictionary<string, IssueData> IssueData { get; set; } = new();
    }
}
