using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.Models;
using CrashDumpAnalyzer.Utilities;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.RegularExpressions;

namespace CrashDumpAnalyzer.Services
{
    public class CallstackQueryService
    {
        public const string DumpType = "CrashDumps";
        private readonly ApplicationDbContext _dbContext;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CallstackQueryService> _logger;
        private readonly int _daysBack;
        private readonly int _maxFixedEntriesToShow;
        private readonly int _lastUploadsDays;
        private readonly int _lastUploadsItems;
        private List<string>? _issueTypes;

        public CallstackQueryService(ApplicationDbContext dbContext, IConfiguration configuration, ILogger<CallstackQueryService> logger)
        {
            _dbContext = dbContext;
            _configuration = configuration;
            _logger = logger;
            _daysBack = configuration.GetValue<int>("ShowEntriesForDaysBack") > 0 ? configuration.GetValue<int>("ShowEntriesForDaysBack") : 180;
            _maxFixedEntriesToShow = configuration.GetValue<int>("ShowMaxFixedEntries") > 0 ? configuration.GetValue<int>("ShowMaxFixedEntries") : 20;
            _lastUploadsDays = configuration.GetValue<int>("LastUploadsDays") > 0 ? configuration.GetValue<int>("LastUploadsDays") : 2;
            _lastUploadsItems = configuration.GetValue<int>("LastUploadsItems") > 0 ? configuration.GetValue<int>("LastUploadsItems") : 20;
        }

        public async Task<List<string>> GetIssueTypes()
        {
            if (_issueTypes != null)
                return _issueTypes;
            var issueTypes = new List<string> { DumpType };
            issueTypes.AddRange(new LogAnalyzer(_logger, _configuration).IssueTypes);
            var storedTypes = await _dbContext.Set<DumpCallstack>().AsNoTracking()
                .Where(cs => cs.LogFileDatas.Count > 0 && !cs.Deleted)
                .Select(cs => cs.ExceptionType).Distinct().ToListAsync();
            foreach (var type in storedTypes)
            {
                if (!issueTypes.Contains(type))
                    issueTypes.Add(type);
            }
            _issueTypes = issueTypes;
            return issueTypes;
        }

        public async Task<List<DumpFileInfo>> FetchLastDumpFileInfos()
        {
            if (_dbContext.DumpFileInfos != null)
            {
                // Calculate the cutoff date for the last x days
                DateTime daysAgo = DateTime.Now.AddDays(-_lastUploadsDays);

                // Fetch the last X items
                var lastXItems = await _dbContext.DumpFileInfos.AsNoTracking()
                    .OrderByDescending(dumpFileInfo => dumpFileInfo.UploadDate)
                    .Take(_lastUploadsItems)
                    .ToListAsync();

                // Fetch all items uploaded in the last two days
                var recentItems = await _dbContext.DumpFileInfos.AsNoTracking()
                    .Where(dumpFileInfo => dumpFileInfo.UploadDate >= daysAgo)
                    .OrderByDescending(dumpFileInfo => dumpFileInfo.UploadDate)
                    .ToListAsync();

                // use the larger list
                var resultList = recentItems.Count > lastXItems.Count ? recentItems : lastXItems;

                // Resolve hostnames for items without a hostname
                foreach (var dumpFileInfo in resultList)
                {
                    if (string.IsNullOrEmpty(dumpFileInfo.UploadedFromHostname) &&
                        !string.IsNullOrWhiteSpace(dumpFileInfo.UploadedFromIp))
                    {
                        try
                        {
                            var myIp = IPAddress.Parse(dumpFileInfo.UploadedFromIp);
                            var getIpHost = await Dns.GetHostEntryAsync(myIp);
                            List<string> compName = [.. getIpHost.HostName.Split('.')];
                            dumpFileInfo.UploadedFromHostname = compName.First();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to resolve hostname for IP {IpAddress}", dumpFileInfo.UploadedFromIp);
                        }
                    }
                }
                return resultList;
            }
            return [];
        }

        public async Task<List<DumpCallstack>> FetchCallStacks(int? id, int? deleted, string searchString = "", int activeTab = -1)
        {
            var issueTypes = await GetIssueTypes();
            if (_dbContext.DumpCallstacks != null)
            {
                List<DumpCallstack>? list = null;
                Dictionary<int, DumpCallstack>? callstackById = null;
                DateTime cutoffDate = DateTime.Now.AddDays(-_daysBack);
                string? issueType = null;
                if (activeTab >= 0 && issueTypes.Count > 1)
                    issueType = issueTypes[activeTab];
                if (id == null)
                {
                    if (string.IsNullOrWhiteSpace(searchString))
                        list = await _dbContext.DumpCallstacks.AsNoTracking()
                            .Include(dumpCallstack => dumpCallstack.DumpInfos)
                            .Include(dumpCallstack => dumpCallstack.LogFileDatas)
                            .ThenInclude(logFileLine => logFileLine.DumpFileInfo)
                            .Where(dumpCallstack => (issueType == null || dumpCallstack.ExceptionType == issueType || (activeTab == 0 && dumpCallstack.LogFileDatas.Count == 0)) &&
                                                    dumpCallstack.Deleted == (deleted > 0) &&
                                                    (dumpCallstack.DumpInfos.Any(dumpInfo => dumpInfo.UploadDate >= cutoffDate) ||
                                                    dumpCallstack.LogFileDatas.Count != 0 ||
                                                    (dumpCallstack.Ticket.Length > 0 && dumpCallstack.FixedVersion.Length == 0) // always include not-fixed callstacks with assigned tickets
                                                    ))
                            .AsSplitQuery()
                            .ToListAsync();
                    else // with search string, include deleted callstacks
                        list = await _dbContext.DumpCallstacks.AsNoTracking()
                            .Include(dumpCallstack => dumpCallstack.DumpInfos)
                            .Include(dumpCallstack => dumpCallstack.LogFileDatas)
                            .ThenInclude(logFileLine => logFileLine.DumpFileInfo)
                            .Where(dumpCallstack => (issueType == null || dumpCallstack.ExceptionType == issueType || (activeTab == 0 && dumpCallstack.LogFileDatas.Count == 0)))
                            .AsSplitQuery()
                            .ToListAsync();
                }
                else
                {
                    list = await _dbContext.DumpCallstacks.AsNoTracking()
                        .Include(dumpCallstack => dumpCallstack.DumpInfos)
                        .Include(dumpCallstack => dumpCallstack.LogFileDatas)
                        .ThenInclude(logFileLine => logFileLine.DumpFileInfo)
                        .Where(dumpCallstack => (issueType == null || dumpCallstack.ExceptionType == issueType || (activeTab == 0 && dumpCallstack.LogFileDatas.Count == 0)) &&
                                                dumpCallstack.DumpCallstackId == id ||
                                                dumpCallstack.LinkedToDumpCallstackId == id)
                        .AsSplitQuery()
                        .ToListAsync();
                }

                if (!string.IsNullOrWhiteSpace(searchString))
                {
                    var splitStrings = searchString.Split([' '], StringSplitOptions.RemoveEmptyEntries);
                    if (searchString.StartsWith('/'))
                        splitStrings = [searchString];

                    foreach (var splitString in splitStrings)
                    {
                        string pattern = string.Empty;
                        if (splitString.StartsWith('/'))
                        {
                            pattern = splitString[1..];
                            try
                            {
                                // Check if the pattern is a valid regex
                                Regex testRegex = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(Constants.RegexTimeoutInSeconds));
                            }
                            catch (Exception)
                            {
                                // Convert wildcard search string to regex pattern
                                pattern = ".*" + Regex.Escape(splitString).Replace("\\*", ".*").Replace("\\?", ".") + ".*";
                            }
                        }
                        else
                            // Convert wildcard search string to regex pattern
                            pattern = ".*" + Regex.Escape(splitString).Replace("\\*", ".*").Replace("\\?", ".") + ".*";
                        Regex regex = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(Constants.RegexTimeoutInSeconds));

                        callstackById = list.ToDictionary(item => item.DumpCallstackId, item => item);

                        list = [.. list.Where(dumpCallstack =>
                            regex.IsMatch(dumpCallstack.ApplicationName) ||
                            regex.IsMatch(dumpCallstack.ApplicationVersion) ||
                            regex.IsMatch(dumpCallstack.FixedVersion) ||
                            regex.IsMatch(dumpCallstack.ExceptionType) ||
                            regex.IsMatch(dumpCallstack.Ticket) ||
                            regex.IsMatch(dumpCallstack.Comment) ||
                            regex.IsMatch(dumpCallstack.Callstack) ||
                            dumpCallstack.DumpInfos.Any(dumpFileInfo =>
                                regex.IsMatch(dumpFileInfo.UploadedFromIp) ||
                                regex.IsMatch(dumpFileInfo.UploadedFromHostname) ||
                                regex.IsMatch(dumpFileInfo.UploadedFromUserEmail) ||
                                regex.IsMatch(dumpFileInfo.UploadedFromUsername) ||
                                regex.IsMatch(dumpFileInfo.VersionResource) ||
                                regex.IsMatch(dumpFileInfo.LogSummary) ||
                                regex.IsMatch(dumpFileInfo.Comment) ||
                                regex.IsMatch(dumpFileInfo.Environment) ||
                                regex.IsMatch(dumpFileInfo.ComputerName) ||
                                regex.IsMatch(dumpFileInfo.Domain)))];
                    }
                }

                foreach (var dumpCallstack in list)
                {
                    // sort individual dumps by upload date
                    if (dumpCallstack.DumpInfos.Count > 0)
                        dumpCallstack.DumpInfos.Sort((a, b) => b.UploadDate.CompareTo(a.UploadDate));
                    // convert the line number string to a list of longs
                    if (dumpCallstack.LogFileDatas.Count > 0)
                    {
                        foreach (var logFileData in dumpCallstack.LogFileDatas)
                        {
                            if (logFileData.LineNumberString != null)
                                logFileData.LineNumbers = [.. logFileData.LineNumberString.Split(',').Select(long.Parse)];
                        }
                    }
                }

                Dictionary<int, List<DumpCallstack>> groupedCallstacks = [];
                List<DumpCallstack> resultList = [];
                foreach (var callstack in list)
                {
                    if (callstack.LinkedToDumpCallstackId != 0)
                        continue;
                    groupedCallstacks[callstack.DumpCallstackId] = [callstack];
                }
                if (list.Count == 1 && groupedCallstacks.Count == 0)
                {
                    groupedCallstacks[list.ElementAt(0).DumpCallstackId] = [list.ElementAt(0)];
                }
                foreach (var callstack in list)
                {
                    if (callstack.LinkedToDumpCallstackId == 0 || callstack.DumpCallstackId == callstack.LinkedToDumpCallstackId)
                        continue;
                    if (groupedCallstacks.TryGetValue(callstack.LinkedToDumpCallstackId, out var value))
                        value.Add(callstack);
                    else if (!string.IsNullOrWhiteSpace(searchString))
                    {
                        if (callstackById != null && callstack.LinkedToDumpCallstackId != 0)
                        {
                            if (callstackById.TryGetValue(callstack.LinkedToDumpCallstackId, out var linkedCallstack))
                                groupedCallstacks[linkedCallstack.DumpCallstackId] = [linkedCallstack, callstack];
                            else
                                groupedCallstacks[callstack.DumpCallstackId] = [callstack];
                        }
                        else
                            groupedCallstacks[callstack.DumpCallstackId] = [callstack];
                    }
                }

                foreach (var group in groupedCallstacks)
                {
                    if (group.Value.Count == 1)
                    {
                        resultList.Add(group.Value[0]);
                    }
                    else
                    {
                        var first = group.Value[0];
                        var tickets = new Dictionary<string, int>();
                        var splitIds = group.Value[0].Ticket.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
                        foreach (var ticket in splitIds)
                        {
                            if (tickets.ContainsKey(ticket))
                                tickets[ticket] += 1;
                            else
                                tickets[ticket] = 1;
                        }
                        for (int i = 1; i < group.Value.Count; i++)
                        {
                            first.DumpInfos.AddRange(group.Value[i].DumpInfos);
                            first.LogFileDatas.AddRange(group.Value[i].LogFileDatas);
                            first.Callstack += "\n---------------------------------------\n" + group.Value[i].Callstack;
                            if (first.ExceptionType != group.Value[i].ExceptionType)
                                first.ExceptionType += "\n\n" + group.Value[i].ExceptionType;
                            // use the lower 'fixed version' of the linked callstacks
                            if (first.FixedVersion != group.Value[i].FixedVersion || first.FixedBuildType != group.Value[i].FixedBuildType)
                            {
                                if (first.FixedVersion.Length == 0)
                                    first.FixedVersion = group.Value[i].FixedVersion;
                                if (group.Value[i].FixedVersion.Length != 0)
                                {
                                    var firstVersion = new SemanticVersion(first.FixedVersion, first.FixedBuildType);
                                    var secondVersion = new SemanticVersion(group.Value[i].FixedVersion, group.Value[i].FixedBuildType);
                                    first.FixedVersion = firstVersion < secondVersion ? first.FixedVersion : group.Value[i].FixedVersion;
                                }
                            }
                            // use the higher 'application version' of the linked callstacks
                            if (first.ApplicationVersion != group.Value[i].ApplicationVersion)
                            {
                                if (first.ApplicationVersion.Length == 0)
                                    first.ApplicationVersion = group.Value[i].ApplicationVersion;
                                if (group.Value[i].ApplicationVersion.Length != 0)
                                {
                                    var firstVersion = new SemanticVersion(first.ApplicationVersion, first.BuildType);
                                    var secondVersion = new SemanticVersion(group.Value[i].ApplicationVersion, group.Value[i].BuildType);
                                    first.ApplicationVersion = firstVersion > secondVersion ? first.ApplicationVersion : group.Value[i].ApplicationVersion;
                                    first.BuildType = firstVersion > secondVersion ? first.BuildType : group.Value[i].BuildType;
                                }
                            }
                            // use tickets
                            splitIds = group.Value[i].Ticket.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
                            foreach (var ticket in splitIds)
                            {
                                if (ticket.Length > 0)
                                {
                                    if (tickets.ContainsKey(ticket))
                                        tickets[ticket] += 1;
                                    else
                                        tickets[ticket] = 1;
                                }
                            }

                        }
                        first.DumpInfos.Sort((a, b) => b.UploadDate.CompareTo(a.UploadDate));
                        first.Ticket = string.Join(", ", tickets.Keys);
                        resultList.Add(first);
                    }
                }
                resultList.Sort((a, b) =>
                {
                    // keep "Unassigned" at the very top
                    if (a.ApplicationName == Constants.UnassignedDumpNames)
                        return -1;
                    if (b.ApplicationName == Constants.UnassignedDumpNames)
                        return 1;

                    // if a callstack is marked as fixed in a specific version
                    // and there is a dump after that version, it should be shown first,
                    // because that indicates that the fix doesn't work
                    var aDumpAfterFixedVersion = !string.IsNullOrEmpty(a.FixedVersion) && HasDumpAfterFixedVersion(a);
                    var bDumpAfterFixedVersion = !string.IsNullOrEmpty(b.FixedVersion) && HasDumpAfterFixedVersion(b);
                    if (aDumpAfterFixedVersion && !bDumpAfterFixedVersion)
                        return -1;
                    if (!aDumpAfterFixedVersion && bDumpAfterFixedVersion)
                        return 1;

                    // if a callstack is marked as deleted, move it to the very end of the list
                    if (a.Deleted != b.Deleted)
                        return a.Deleted ? 1 : -1;
                    // if a callstack is marked as fixed, move it to the end of the list
                    if (!string.IsNullOrEmpty(a.FixedVersion) && string.IsNullOrEmpty(b.FixedVersion))
                        return 1;
                    if (string.IsNullOrEmpty(a.FixedVersion) && !string.IsNullOrEmpty(b.FixedVersion))
                        return -1;

                    if (string.IsNullOrEmpty(a.FixedVersion) && string.IsNullOrEmpty(b.FixedVersion))
                    {
                        // show callstacks which don't have a ticket assigned first
                        if (string.IsNullOrEmpty(a.Ticket) && !string.IsNullOrEmpty(b.Ticket))
                            return -1;
                        if (!string.IsNullOrEmpty(a.Ticket) && string.IsNullOrEmpty(b.Ticket))
                            return 1;
                    }
                    if (activeTab > 0)
                    {
                        // calculate the number of issues found in the callstack
                        // and sort by that number
                        var aIssues = a.LogFileDatas.Sum(logFileData => logFileData.LineNumbers?.Count ?? 0);
                        var bIssues = b.LogFileDatas.Sum(logFileData => logFileData.LineNumbers?.Count ?? 0);
                        if (aIssues != bIssues)
                            return bIssues - aIssues; // sort by number of issues found
                    }
                    try
                    {
                        var aUploadDate = a.DumpInfos.Count > 0 ? a.DumpInfos.Max(dumpInfo => dumpInfo.UploadDate) : (a.LogFileDatas.Count > 0 ? a.LogFileDatas.Max(logFileData => logFileData.DumpFileInfo != null ? logFileData.DumpFileInfo.UploadDate : logFileData.LatestTime) : DateTime.MinValue);
                        var bUploadDate = b.DumpInfos.Count > 0 ? b.DumpInfos.Max(dumpInfo => dumpInfo.UploadDate) : (b.LogFileDatas.Count > 0 ? b.LogFileDatas.Max(logFileData => logFileData.DumpFileInfo != null ? logFileData.DumpFileInfo.UploadDate : logFileData.LatestTime) : DateTime.MinValue);

                        if (aUploadDate != bUploadDate)
                            return bUploadDate.CompareTo(aUploadDate); // sort by date of last upload, so it's easy to find the just uploaded ones
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error sorting list");
                    }

                    // sort by number of dumps - the more dumps with the same callstack the more urgent it is to fix
                    return b.DumpInfos.Count - a.DumpInfos.Count;
                });

                if (!string.IsNullOrEmpty(searchString))
                    return resultList;
                // find the index where the fixed callstacks start
                int fixedIndex = resultList.FindIndex(callstack => !string.IsNullOrEmpty(callstack.FixedVersion) &&
                                new SemanticVersion(callstack.FixedVersion, callstack.FixedBuildType) > new SemanticVersion(callstack.ApplicationVersion, callstack.BuildType));
                if (fixedIndex == -1)
                    fixedIndex = resultList.Count;
                return [.. resultList.Take(fixedIndex + _maxFixedEntriesToShow)];
            }
            return [];
        }

        private static bool HasDumpAfterFixedVersion(DumpCallstack dumpCallstack)
        {
            var appVersion = new SemanticVersion(dumpCallstack.ApplicationVersion, dumpCallstack.BuildType < 0 ? BuildTypes.ParseBuildType("") : dumpCallstack.BuildType);
            var fixedVersion = new SemanticVersion(dumpCallstack.FixedVersion, dumpCallstack.FixedBuildType);
            return appVersion > fixedVersion;
        }
    }
}
