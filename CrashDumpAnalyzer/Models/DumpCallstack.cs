﻿using System.ComponentModel;

namespace CrashDumpAnalyzer.Models
{
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
    }
}
