using System.ComponentModel.DataAnnotations.Schema;

namespace CrashDumpAnalyzer.Models
{
    public class LogFileData
    {
        public int LogFileDataId { get; set; }
        public string? LineNumberString { get; set; }
        [NotMapped]
        public List<long>? LineNumbers { get; set; }
        public DateTime LatestTime { get; set; }
        public DumpFileInfo? DumpFileInfo { get; set; }
    }
}
