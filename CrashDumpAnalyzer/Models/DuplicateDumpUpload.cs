namespace CrashDumpAnalyzer.Models
{
    public class DuplicateDumpUpload
    {
        public int DuplicateDumpUploadId { get; set; }
        public int DumpFileInfoId { get; set; }
        public DumpFileInfo DumpFileInfo { get; set; } = null!;
        public DateTime UploadDate { get; set; }
        public string UploadedFromIp { get; set; } = string.Empty;
    }
}