namespace CrashDumpAnalyzer.Models
{
    public class StatisticsData
    {
        public required List<string> IssueTypes { get; set; } = new();
        public required Dictionary<string, int> OpenCallstacks { get; set; } = new();
        public required Dictionary<string, int> OpenCallstacksWithoutTickets { get; set; } = new();
        public required Dictionary<string, int> ClosedCallstacks { get; set; } = new();
        public required Dictionary<string, int> CallstacksAssignedToExistingCallstacks { get; set; } = new();

        public required Dictionary<string, List<Tuple<int, int>>> NewCallstacksPerWeek { get; set; } = new();

        public required Dictionary<string, int> NumberOfFiles { get; set; } = new();
        public required Dictionary<string, long> TotalFileSize { get; set; } = new();
    }
}
