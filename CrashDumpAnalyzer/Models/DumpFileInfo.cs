﻿using System.ComponentModel.DataAnnotations.Schema;

namespace CrashDumpAnalyzer.Models
{
    public class DumpFileInfo
    {
        public int DumpFileInfoId { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; } = 0;
        public DateTime UploadDate { get; set; }
        public DateTime DumpTime { get; set; }
        public string ApplicationName { get; set; } = string.Empty;
        public string ApplicationVersion { get; set; } = string.Empty;
        public string CallStack { get; set; } = string.Empty;
        public string ExceptionType { get; set; } = string.Empty;
        public string UploadedFromIp { get; set; } = string.Empty;
        public string UploadedFromHostname { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string ComputerName { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string VersionResource { get; set; } = string.Empty;

        public int DumpCallstackId { get; set; }
        public DumpCallstack? DumpCallstack { get; set; }
    }
}
