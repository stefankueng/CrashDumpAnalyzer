
namespace CrashDumpAnalyzer.Models
{
    public class IndexPageData
    {

        public required IEnumerable<CrashDumpAnalyzer.Models.DumpCallstack> Callstacks { get; set; }
        public required IEnumerable<CrashDumpAnalyzer.Models.DumpFileInfo> UploadedDumps { get; set; }
        public required string ActiveFilterString { get; set; }
    }
}
