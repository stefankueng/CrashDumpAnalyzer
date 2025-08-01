﻿using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace CrashDumpAnalyzer.Models
{
    /// <summary>
    /// Represents a dump file with extracted information, or a log file.
    /// If a log file contains entries for multiple versions, then the highest version is used.
    /// </summary>
    [Index(nameof(FilePath))]
    [Index(nameof(DumpCallstackId))]
    [Index(nameof(UploadDate))]
    [Index(nameof(ApplicationName))]
    [Index(nameof(UploadDate), nameof(DumpCallstackId))]
    [Index(nameof(UploadedFromIp))]
    [Index(nameof(UploadedFromHostname))]
    [Index(nameof(UploadedFromUserEmail))]
    [Index(nameof(UploadedFromUsername))]
    [Index(nameof(Environment))]
    [Index(nameof(ComputerName))]
    [Index(nameof(Domain))]
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
        public string LogSummary { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public string UploadedFromUsername { get; set; } = string.Empty;
        public string UploadedFromUserEmail { get; set; } = string.Empty;

        public int DumpCallstackId { get; set; }
        public DumpCallstack? DumpCallstack { get; set; }
    }
}
