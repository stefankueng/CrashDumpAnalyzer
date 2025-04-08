using CrashDumpAnalyzer.IssueTrackers.Data;

namespace CrashDumpAnalyzer.Models
{
    public class IndexPageData
    {
        public required IEnumerable<CrashDumpAnalyzer.Models.DumpCallstack> Callstacks { get; set; }
        public required IEnumerable<CrashDumpAnalyzer.Models.DumpFileInfo> UploadedDumps { get; set; }
        public required string ActiveFilterString { get; set; }
        public required int ActiveTab { get; set; }
        public required List<string> Tabs { get; set; }
        public Dictionary<string, IssueData> IssueData { get; set; } = new();
    }
}
