using CrashDumpAnalyzer.IssueTrackers.Data;

namespace CrashDumpAnalyzer.Models
{
    public class TicketGroupData
    {
        public string Ticket { get; set; } = string.Empty;
        public List<DumpCallstack> Callstacks { get; set; } = [];
        public int RecentDumpCount { get; set; }
        public Dictionary<string, int> CommentCounts { get; set; } = [];
        public string HighestFixedVersion { get; set; } = string.Empty;
        public int HighestFixedBuildType { get; set; } = -1;
    }

    public class RecentDumpsData
    {
        public List<TicketGroupData> TicketGroups { get; set; } = [];
        public Dictionary<string, IssueData> IssueData { get; set; } = [];
        public int WeeksBack { get; set; } = 4;
    }
}
