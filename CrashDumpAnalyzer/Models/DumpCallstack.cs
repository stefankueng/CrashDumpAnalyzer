namespace CrashDumpAnalyzer.Models
{
	public class DumpCallstack
	{
		public int DumpCallstackId { get; set; }
		public string ApplicationName { get; set; } = string.Empty;
		public string ApplicationVersion { get; set; } = string.Empty;
		public string FixedVersion { get; set; } = string.Empty;
		public string ExceptionType { get; set; } = string.Empty;
		public string Callstack { get; set; } = string.Empty;
		public List<DumpFileInfo> DumpInfos { get; set; } = new ();
	}
}
