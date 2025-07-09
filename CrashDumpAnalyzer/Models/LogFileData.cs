using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace CrashDumpAnalyzer.Models
{
    [Index(nameof(LatestTime))]
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
