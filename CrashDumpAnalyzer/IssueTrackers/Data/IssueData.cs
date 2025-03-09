using CrashDumpAnalyzer.Utilities;

namespace CrashDumpAnalyzer.IssueTrackers.Data
{
    public class IssueData
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string StateColor { get; set; } = string.Empty;
    }
}
