using CrashDumpAnalyzer.IssueTrackers.Data;
using CrashDumpAnalyzer.Models;
using System.Collections.Generic;
using System.Linq;

namespace CrashDumpAnalyzer.Models
{
    public class GroupedDumpData
    {
        public string GroupKey { get; set; } = string.Empty;
        public List<DumpCallstack> Callstacks { get; set; } = new();
        public int DumpCount { get; set; }
        public Dictionary<string, int> CommentCounts { get; set; } = new();
        public string HighestFixedVersion { get; set; } = string.Empty;
        public int HighestFixedBuildType { get; set; } = -1;
        public List<string> Tickets => Callstacks.SelectMany(cs => cs.Ticket?.Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries) ?? new string[0])
                                                .Distinct()
                                                .OrderBy(t => t)
                                                .ToList();
        public List<string> FixedVersions => Callstacks.Where(cs => !string.IsNullOrEmpty(cs.FixedVersion))
                                                       .Select(cs => cs.FixedVersion)
                                                       .Distinct()
                                                       .OrderByDescending(v => v)
                                                       .ToList();
    }

    public class GroupedDumpsData
    {
        public List<GroupedDumpData> Groups { get; set; } = new();
        public int WeeksBack { get; set; } = 4;
        public Dictionary<string, IssueData> IssueData { get; set; } = new();
    }
}
