using System.ComponentModel.DataAnnotations;

namespace CrashDumpAnalyzer.Models
{
    public class SettingsModel
    {
        [Required]
        public string Revision { get; set; } = string.Empty;
        [Required]
        public string General { get; set; } = string.Empty;
        [Required]
        public string StaticFolders { get; set; } = string.Empty;
        [Required]
        public string LogFileAnalyzer { get; set; } = string.Empty;
        [Required]
        public string IssueTracker { get; set; } = string.Empty;
        [Required]
        public string Smtp { get; set; } = string.Empty;
        [Required]
        public string BuildTypes { get; set; } = string.Empty;
        [Required]
        public string MinVersions { get; set; } = string.Empty;
        [DataType(DataType.Password)]
        public string? IssuePassword { get; set; }
        [DataType(DataType.Password)]
        public string? IssueBearerToken { get; set; }
        [DataType(DataType.Password)]
        public string? IssueToken { get; set; }
        [DataType(DataType.Password)]
        public string? SmtpPassword { get; set; }
        public bool ClearSmtpPassword { get; set; }
        public bool ClearIssuePassword { get; set; }
        public bool ClearIssueBearerToken { get; set; }
        public bool ClearIssueToken { get; set; }
    }
}
