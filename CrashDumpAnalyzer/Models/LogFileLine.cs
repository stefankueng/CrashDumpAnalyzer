namespace CrashDumpAnalyzer.Models
{
    public class LogFileLine
    {
        public int LogFileLineId { get; set; }
        public long LineNumber { get; set; }
        public DateTime Time { get; set; }
        public DumpFileInfo? DumpFileInfo { get; set; }
    }
}
