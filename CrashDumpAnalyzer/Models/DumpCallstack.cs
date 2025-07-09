using Microsoft.EntityFrameworkCore;
using System.ComponentModel;

namespace CrashDumpAnalyzer.Models
{
    /// <summary>
    /// Represents a specific crash identified by its callstack, or an issue extracted from a log file.
    /// The log file issue text is stored in the Callstack property and
    /// therefore needs to be unique so that the same issues have the same text.
    /// </summary>
    [Index(nameof(CleanCallstack))]
    [Index(nameof(ApplicationName))]
    [Index(nameof(LinkedToDumpCallstackId))]
    [Index(nameof(Deleted))]
    [Index(nameof(ApplicationName), nameof(Deleted))]
    [Index(nameof(ExceptionType), nameof(Deleted))]
    [Index(nameof(FixedVersion), nameof(Deleted))]
    [Index(nameof(ExceptionType))]
    [Index(nameof(ApplicationName), nameof(ExceptionType))]
    [Index(nameof(Deleted), nameof(FixedVersion), nameof(LinkedToDumpCallstackId))]
    [Index(nameof(Deleted), nameof(LinkedToDumpCallstackId))]
    [Index(nameof(Ticket))]
    [Index(nameof(FixedVersion), nameof(FixedBuildType))]
    [Index(nameof(ApplicationVersion), nameof(BuildType))]
    public class DumpCallstack
    {
        public int DumpCallstackId { get; set; }
        public string ApplicationName { get; set; } = string.Empty;
        public string ApplicationVersion { get; set; } = string.Empty;
        public int BuildType { get; set; } = -1;
        public string FixedVersion { get; set; } = string.Empty;
        public int FixedBuildType { get; set; } = -1;
        public string ExceptionType { get; set; } = string.Empty;
        public string Ticket { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public string Callstack { get; set; } = string.Empty;
        public string CleanCallstack { get; set; } = string.Empty;
        public int LinkedToDumpCallstackId { get; set; } = 0;
        public bool Deleted { get; set; } = false;
        public List<DumpFileInfo> DumpInfos { get; set; } = new ();
        public List<LogFileData> LogFileDatas { get; set; } = new();
    }
}
