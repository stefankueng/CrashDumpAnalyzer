using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.Models;
using CrashDumpAnalyzer.Utilities;
using Microsoft.EntityFrameworkCore;
using CrashDumpAnalyzer.IssueTrackers.Data;
using CrashDumpAnalyzer.IssueTrackers.Interfaces;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CrashDumpAnalyzer.Services
{
    public class HomePageService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly CallstackQueryService _queries;
        private readonly IIssueTracker? _issueTracker;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<HomePageService> _logger;
        private readonly Regex _groupedDumpsRegex;

        public HomePageService(ApplicationDbContext dbContext, CallstackQueryService queries, IHttpClientFactory httpClientFactory,
            IConfiguration configuration, ILogger<HomePageService> logger, IIssueTracker? issueTracker = null)
        {
            _dbContext = dbContext;
            _queries = queries;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _issueTracker = issueTracker;
            _groupedDumpsRegex = new Regex(configuration["DumpCommentGroupingRegex"] ?? "(?<GroupKey>.+)",
                RegexOptions.Compiled, TimeSpan.FromSeconds(Constants.RegexTimeoutInSeconds));
        }

        public async Task<IndexPageData> GetIndex(int? deleted, string searchString, int activeTab)
        {
            var callstacks = await _queries.FetchCallStacks(null, deleted, searchString, activeTab);
            return new IndexPageData
            {
                Callstacks = callstacks,
                UploadedDumps = await _queries.FetchLastDumpFileInfos(),
                ActiveFilterString = searchString,
                IssueData = await GetIssueData(callstacks),
                ActiveTab = activeTab,
                Tabs = await _queries.GetIssueTypes()
            };
        }

        public async Task<DumpPageData?> GetDump(int callstackId)
        {
            var callstacks = await _queries.FetchCallStacks(callstackId, null);
            if (callstacks.Count != 1)
                return null;
            if (callstacks[0].LinkedToDumpCallstackId != 0)
                callstacks = await _queries.FetchCallStacks(callstacks[0].LinkedToDumpCallstackId, null);
            if (callstacks.Count != 1)
                return null;
            return new DumpPageData { Callstack = callstacks[0], IssueData = await GetIssueData(callstacks) };
        }

        public async Task<StatisticsData?> GetStatistics()
        {
            var _issueTypes = await _queries.GetIssueTypes();
            if (_dbContext.DumpCallstacks != null)
            {
                // get all statistics from the db
                var callStacks = await _dbContext.DumpCallstacks.AsNoTracking()
                    .Where(callstack => callstack.ApplicationName != Constants.UnassignedDumpNames)
                    .Include(callstack => callstack.DumpInfos)
                    .AsSplitQuery()
                    .Include(callstack => callstack.LogFileDatas)
                    .ToListAsync();


                var openCallstacks = callStacks
                    .Where(callstack => !callstack.Deleted && string.IsNullOrEmpty(callstack.FixedVersion) && callstack.LinkedToDumpCallstackId == 0)
                    .GroupBy(callstack => _issueTypes.Contains(callstack.ExceptionType) ? callstack.ExceptionType : CallstackQueryService.DumpType)
                    .ToDictionary(group => group.Key, group => group.Count());
                var openCallstacksWithoutTickets = callStacks
                    .Where(callstack => !callstack.Deleted && string.IsNullOrEmpty(callstack.FixedVersion) && string.IsNullOrEmpty(callstack.Ticket) && callstack.LinkedToDumpCallstackId == 0)
                    .GroupBy(callstack => _issueTypes.Contains(callstack.ExceptionType) ? callstack.ExceptionType : CallstackQueryService.DumpType)
                    .ToDictionary(group => group.Key, group => group.Count());
                var closedCallstacks = callStacks
                    .Where(callstack => callstack.Deleted || !string.IsNullOrEmpty(callstack.FixedVersion) && callstack.LinkedToDumpCallstackId == 0)
                    .GroupBy(callstack => _issueTypes.Contains(callstack.ExceptionType) ? callstack.ExceptionType : CallstackQueryService.DumpType)
                    .ToDictionary(group => group.Key, group => group.Count());


                var callstacksAssignedToExistingCallstacks = callStacks
                    .GroupBy(callstack => _issueTypes.Contains(callstack.ExceptionType) ? callstack.ExceptionType : CallstackQueryService.DumpType)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .Select(callstack => new
                            {
                                AdjustedDumpCount = (callstack.DumpInfos?.Count ?? 1) - 1,
                                LinkedDumpCount = callstack.LinkedToDumpCallstackId != 0 ? 1 : 0,
                                AdjustedLineCount = Math.Max((callstack.LogFileDatas?.Sum(logFileData => logFileData.LineNumbers?.Count ?? 0) ?? 0) - 1, 0)
                            })
                            .Where(adjusted => adjusted.AdjustedDumpCount > 0 || adjusted.LinkedDumpCount > 0 || adjusted.AdjustedLineCount > 0)
                            .Aggregate(0, (total, adjusted) => total + adjusted.AdjustedDumpCount + adjusted.LinkedDumpCount + adjusted.AdjustedLineCount)
                    );



                var newCallstacksPerWeek = callStacks
                    .Where(callstack => callstack.DumpInfos != null && callstack.DumpInfos.Count != 0)
                    .GroupBy(callstack => _issueTypes.Contains(callstack.ExceptionType) ? callstack.ExceptionType : CallstackQueryService.DumpType)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .SelectMany(callstack => callstack.DumpInfos.Select(dumpInfo => new
                            {
                                Week = CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(
                                    dumpInfo.UploadDate, CalendarWeekRule.FirstDay, DayOfWeek.Monday) + dumpInfo.UploadDate.Year * 1000,
                                Count = 1
                            }))
                            .GroupBy(x => x.Week)
                            .Select(g => Tuple.Create(g.Key, g.Count()))
                            .ToList()
                    );
                var numberOfFiles = callStacks
                    .Where(callstack => !callstack.Deleted)
                    .GroupBy(callstack => _issueTypes.Contains(callstack.ExceptionType) ? callstack.ExceptionType : CallstackQueryService.DumpType)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Sum(callstack =>
                            (callstack.DumpInfos?.Count(dumpInfo => !string.IsNullOrEmpty(dumpInfo.FilePath)) ?? 0) +
                            (callstack.LogFileDatas?.Count(logFileData => logFileData.DumpFileInfo != null && !string.IsNullOrEmpty(logFileData.DumpFileInfo.FilePath)) ?? 0)
                        )
                    );

                var totalFileSize = callStacks
                    .GroupBy(callstack => _issueTypes.Contains(callstack.ExceptionType) ? callstack.ExceptionType : CallstackQueryService.DumpType)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Sum(callstack =>
                        {
                            long fileSizeSelector(DumpFileInfo dumpInfo) => dumpInfo.FileSize;
                            long logFileDataFileSizeSelector(LogFileData logFileData) => logFileData.DumpFileInfo!.FileSize;
                            return (callstack.DumpInfos?.Where(dumpInfo => !string.IsNullOrEmpty(dumpInfo.FilePath)).Sum(fileSizeSelector) ?? 0) +
                                                        (callstack.LogFileDatas?.Where(logFileData => logFileData.DumpFileInfo != null && !string.IsNullOrEmpty(logFileData.DumpFileInfo.FilePath)).Sum(selector: logFileDataFileSizeSelector) ?? 0);
                        })
                    );



                var statisticsData = new StatisticsData
                {
                    IssueTypes = _issueTypes,
                    OpenCallstacks = openCallstacks,
                    OpenCallstacksWithoutTickets = openCallstacksWithoutTickets,
                    ClosedCallstacks = closedCallstacks,
                    CallstacksAssignedToExistingCallstacks = callstacksAssignedToExistingCallstacks,
                    NewCallstacksPerWeek = newCallstacksPerWeek,
                    NumberOfFiles = numberOfFiles,
                    TotalFileSize = totalFileSize,
                };
                return statisticsData;
            }
            return null;
        }
        public async Task<GroupedDumpsData?> GetGroupedDumps(int weeksBack = 8)
        {
            if (_dbContext.DumpCallstacks != null)
            {
                var dateLimit = DateTime.UtcNow.AddDays(-7 * weeksBack);

                // Get unlinked callstacks with recent dumps
                var unlinkedCallstacks = await _dbContext.DumpCallstacks
                    .AsNoTracking()
                    .Include(c => c.DumpInfos)
                    .Where(callstack => (!string.IsNullOrEmpty(callstack.Ticket) || !string.IsNullOrEmpty(callstack.Comment)) &&
                                       callstack.LinkedToDumpCallstackId == 0 &&
                                       callstack.ApplicationName != Constants.UnassignedDumpNames &&
                                       callstack.DumpInfos.Any(dumpInfo => dumpInfo.DumpTime >= dateLimit))
                    .ToListAsync();

                // Get linked callstacks with recent dumps
                var linkedCallstacks = await _dbContext.DumpCallstacks
                    .AsNoTracking()
                    .Include(c => c.DumpInfos)
                    .Where(callstack => (!string.IsNullOrEmpty(callstack.Ticket) || !string.IsNullOrEmpty(callstack.Comment)) &&
                                       callstack.LinkedToDumpCallstackId != 0 &&
                                       callstack.ApplicationName != Constants.UnassignedDumpNames &&
                                       callstack.DumpInfos.Any(dumpInfo => dumpInfo.DumpTime >= dateLimit))
                    .ToListAsync();

                // Get parent callstacks of linked callstacks (even if their dumps are older than dateLimit)
                var parentCallstackIds = linkedCallstacks
                    .Select(cs => cs.LinkedToDumpCallstackId)
                    .Distinct()
                    .ToList();

                var parentCallstacks = new List<DumpCallstack>();
                if (parentCallstackIds.Count > 0)
                {
                    parentCallstacks = await _dbContext.DumpCallstacks
                        .AsNoTracking()
                        .Include(c => c.DumpInfos)
                        .Where(cs => parentCallstackIds.Contains(cs.DumpCallstackId))
                        .ToListAsync();
                }

                // Combine unlinked and parent callstacks, removing duplicates
                var callstacks = unlinkedCallstacks
                    .Concat(parentCallstacks)
                    .DistinctBy(cs => cs.DumpCallstackId)
                    .ToList();

                var dumpFileInfos = callstacks
                    .SelectMany(cs => cs.DumpInfos
                        .Where(dump => dump.UploadDate >= dateLimit)
                        .Select(dump => new { Callstack = cs, Dump = dump }))
                    .ToList();

                var groups = dumpFileInfos
                    .GroupBy(x =>
                    {
                        var match = _groupedDumpsRegex.Match(x.Dump.Comment);
                        var key = match.Success && match.Groups["GroupKey"].Value.Trim() != "" ? match.Groups["GroupKey"].Value.Trim() : "unknown";
                        return key;
                    })
                    .Select(g => new GroupedDumpData
                    {
                        GroupKey = g.Key,
                        Callstacks = [.. g.Select(x => x.Callstack).Distinct()],
                        DumpCount = g.Count(),
                        CommentCounts = g.GroupBy(x => x.Dump.Comment).ToDictionary(x => x.Key, x => x.Count()),
                        HighestFixedVersion = g.Select(x => x.Callstack).Where(cs => !string.IsNullOrEmpty(cs.FixedVersion)).OrderByDescending(cs => cs.FixedVersion).FirstOrDefault()?.FixedVersion ?? string.Empty,
                        HighestFixedBuildType = g.Select(x => x.Callstack).Where(cs => cs.FixedBuildType >= 0).OrderByDescending(cs => cs.FixedBuildType).FirstOrDefault()?.FixedBuildType ?? -1
                    })
                    .OrderBy(g => g.GroupKey == "unknown" ? 1 : 0)
                    .ThenByDescending(g => g.DumpCount)
                    .ToList();
                var issueData = await GetIssueData(callstacks);

                var model = new GroupedDumpsData
                {
                    Groups = groups,
                    WeeksBack = weeksBack,
                    IssueData = issueData
                };
                return model;
            }
            return null;
        }
        private async Task<Dictionary<string, IssueData>> GetIssueData(List<DumpCallstack> callstacks)
        {
            if (_issueTracker != null)
            {
                var issueIds = callstacks.Select(callstack => callstack.Ticket.Trim()).Distinct().ToList();
                // split issueIds that are separated by space or comma
                var updatedIssueIds = new List<string>();
                foreach (var issueId in issueIds)
                {
                    var splitIds = issueId.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
                    updatedIssueIds.AddRange(splitIds);
                }
                issueIds = [.. updatedIssueIds.Distinct().Where(id => !string.IsNullOrEmpty(id))];
                issueIds = [.. issueIds.Distinct()];

                using var httpClient = _httpClientFactory.CreateClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
                cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30-second timeout
                try
                {
                    return await _issueTracker.GetIssueDataAsync(httpClient, issueIds, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Issue tracker request timed out");
                    return []; // Return empty data instead of crashing
                }
            }
            return [];
        }
    }
}
