using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CrashDumpAnalyzer.Utilities
{
    public class LogIssue
    {
        public string ApplicationName { get; set; } = string.Empty;
        public string ApplicationVersion { get; set; } = string.Empty;
        public int BuildType { get; set; } = -1;
        public string IssueType { get; set; } = string.Empty;
        public string IssueText { get; set; } = string.Empty;
        public string IssueTextClean { get; set; } = string.Empty;
        public override int GetHashCode()
        {
            return (ApplicationName + ApplicationVersion + BuildType + IssueType + IssueText + IssueTextClean).GetHashCode();
        }
        public override bool Equals(object? obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }
            var other = (LogIssue)obj;
            return ApplicationName == other.ApplicationName &&
                ApplicationVersion == other.ApplicationVersion &&
                BuildType == other.BuildType &&
                IssueType == other.IssueType &&
                IssueText == other.IssueText &&
                IssueTextClean == other.IssueTextClean;
        }
    }

    public class SpecificLogIssue 
    {
        public LogIssue LogIssue { get; set; } = new LogIssue();
        public long LineNumber { get; set; } = 0;
        public DateTime Time { get; set; } = DateTime.MinValue;
    }

    public class LogAnalyzer
    {
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;
        private readonly Regex _dateTimeRegex;
        private readonly Regex _applicationNameRegex;
        private readonly Regex _versionRegex;
        private readonly Regex _buildTypeRegex;
        private readonly string _dateTimeFormat;
        private readonly Dictionary<string, Regex> _logIssueTypeRegexes;

        public LogAnalyzer(ILogger logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            var logFileAnalyzerSection = _configuration.GetSection("LogFileAnalyzer");
            _dateTimeRegex = new Regex(logFileAnalyzerSection.GetValue<string>("DateTimeRegex", @"^(\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2})"), RegexOptions.Compiled | RegexOptions.IgnoreCase);
            _applicationNameRegex = new Regex(logFileAnalyzerSection.GetValue<string>("ApplicationNameRegex", @"Application: (.*)"), RegexOptions.Compiled | RegexOptions.IgnoreCase);
            _versionRegex = new Regex(logFileAnalyzerSection.GetValue<string>("VersionRegex", @"Version: (.*)"), RegexOptions.Compiled | RegexOptions.IgnoreCase);
            _buildTypeRegex = new Regex(logFileAnalyzerSection.GetValue<string>("BuildTypeRegex", @"Build Type: (.*)"), RegexOptions.Compiled | RegexOptions.IgnoreCase);
            _dateTimeFormat = logFileAnalyzerSection.GetValue<string>("DateTimeFormat", "yyyy/MM/dd HH:mm:ss");
            _logIssueTypeRegexes = new Dictionary<string, Regex>();
            logFileAnalyzerSection.GetSection("LogIssueTypes").GetChildren().ToList().ForEach(issueType =>
            {
                var type = issueType.Key;
                var regexString = issueType.GetValue<string>("Regex", string.Empty);
                if (!string.IsNullOrEmpty(regexString))
                {
                    _logIssueTypeRegexes.Add(type, new Regex(regexString, RegexOptions.Compiled | RegexOptions.IgnoreCase));
                }
            });

        }
        public async Task<Dictionary<LogIssue, List<(DateTime date, long lineNumber)>>> Analyze(string logFilePath, CancellationToken token)
        {
            string output = await File.ReadAllTextAsync(logFilePath, Encoding.Default, token);

            string versionString = string.Empty;
            string buildTypeString = string.Empty;
            string applicationName = string.Empty;
            List<SpecificLogIssue> issues = new List<SpecificLogIssue>();
            long lineCount = 0;
            foreach (var lineStringOrig in output.Split(["\n"], StringSplitOptions.None))
            {
                ++lineCount;
                var lineString = lineStringOrig.Trim();

                bool versionChanged = false;
                var versionMatch = _versionRegex.Match(lineString);
                if (versionMatch.Success)
                {
                    versionChanged = versionString != versionMatch.Groups[1].Value;
                    versionString = versionMatch.Groups[1].Value;
                }

                var buildTypeMatch = _buildTypeRegex.Match(lineString);
                if (buildTypeMatch.Success)
                {
                    versionChanged = versionString != versionMatch.Groups[1].Value;
                    buildTypeString = buildTypeMatch.Groups[1].Value;
                }
                if (versionChanged)
                {
                    // update all previous entries that don't have a version yet
                    foreach (var logIssue in issues)
                    {
                        if (logIssue.LogIssue.ApplicationVersion == string.Empty)
                        {
                            logIssue.LogIssue.ApplicationVersion = versionString;
                        }
                        else if (logIssue.LogIssue.BuildType == -1)
                        {
                            logIssue.LogIssue.BuildType = BuildTypes.ParseBuildType(buildTypeString);
                        }
                        else
                            break;
                    }
                }

                bool applicationNameChanged = false;
                var applicationNameMatch = _applicationNameRegex.Match(lineString);
                if (applicationNameMatch.Success)
                {
                    applicationNameChanged = applicationName != applicationNameMatch.Groups[1].Value;
                    applicationName = applicationNameMatch.Groups[1].Value;
                }
                if (applicationNameChanged)
                {
                    // update all previous entries that don't have an application name yet
                    foreach (var logIssue in issues)
                    {
                        if (logIssue.LogIssue.ApplicationName == string.Empty)
                        {
                            logIssue.LogIssue.ApplicationName = applicationName;
                        }
                        else
                            break;
                    }
                }
                DateTime? dateTime = null;
                var dateTimeMatch = _dateTimeRegex.Match(lineString);
                if (dateTimeMatch.Success)
                {
                    try
                    {
                        dateTime = DateTime.ParseExact(dateTimeMatch.Groups[1].Value, _dateTimeFormat, CultureInfo.InvariantCulture);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error parsing time");
                    }
                }

                foreach (var logIssueTypeRegex in _logIssueTypeRegexes)
                {
                    var logIssueType = logIssueTypeRegex.Key;
                    var logIssueRegex = logIssueTypeRegex.Value;
                    var logIssueMatch = logIssueRegex.Match(lineString);
                    if (logIssueMatch.Success)
                    {
                        var logIssue = new LogIssue
                        {
                            ApplicationName = applicationName,
                            ApplicationVersion = versionString,
                            BuildType = BuildTypes.ParseBuildType(buildTypeString),
                            IssueType = logIssueType,
                            IssueText = logIssueMatch.Groups[1].Value
                        };
                        logIssue.IssueTextClean = logIssue.IssueText;
                        for (int i = 2; i < logIssueMatch.Groups.Count; i++)
                        {
                            logIssue.IssueTextClean = logIssue.IssueTextClean.Replace(logIssueMatch.Groups[i].Value, string.Empty);
                        }

                        issues.Add(new SpecificLogIssue
                        {
                            LogIssue = logIssue,
                            LineNumber = lineCount,
                            Time = dateTime ?? DateTime.MinValue
                        });
                    }
                }
            }
            // sort issues by Time
            issues.Sort((a, b) => a.Time.CompareTo(b.Time));

            var logIssues = new Dictionary<LogIssue, List<(DateTime date, long lineNumber)>>();
            foreach (var issue in issues)
            {
                if (!logIssues.ContainsKey(issue.LogIssue))
                {
                    logIssues[issue.LogIssue] = new List<(DateTime date, long lineNumber)>();
                }
                logIssues[issue.LogIssue].Add((issue.Time, issue.LineNumber));
            }
            return logIssues;
        }
    }
}
