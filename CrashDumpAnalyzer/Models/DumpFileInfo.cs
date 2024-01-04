using System.ComponentModel.DataAnnotations.Schema;

namespace CrashDumpAnalyzer.Models
{
	public class DumpFileInfo
	{
		public int DumpFileInfoId { get; set; }
		public string FilePath { get; set; } = string.Empty;
		public long FileSize { get; set; } = 0;
		public DateTime UploadDate { get; set; }
		public string ApplicationName { get; set; } = string.Empty;
		public string ApplicationVersion { get; set; } = string.Empty;
		public string CallStack { get; set; } = string.Empty;
		public string ExceptionType { get; set; } = string.Empty;

		public int DumpCallstackId { get; set; }
		public DumpCallstack? DumpCallstack { get; set; }
	}
}
