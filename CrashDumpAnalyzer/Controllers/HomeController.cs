using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.Models;

using Microsoft.AspNetCore.Mvc;

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using CrashDumpAnalyzer.Utilities;
using System.Configuration;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using CrashDumpAnalyzer.IssueTrackers.Interfaces;
using CrashDumpAnalyzer.IssueTrackers.Data;

namespace CrashDumpAnalyzer.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IIssueTracker _issueTracker;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ApplicationDbContext _dbContext;
        private readonly int _daysBack;

        public HomeController(IConfiguration configuration,
            ILogger<HomeController> logger,
            IIssueTracker issueTracker,
            IHttpClientFactory httpClientFactory,
            ApplicationDbContext context)
        {
            _logger = logger;
            _configuration = configuration;
            _issueTracker = issueTracker;
            _httpClientFactory = httpClientFactory;
            _dbContext = context;
            _daysBack = configuration.GetValue<int>("ShowEntriesForDaysBack") > 0 ? configuration.GetValue<int>("ShowEntriesForDaysBack") : 180;
            Constants.TicketBaseUrl = configuration.GetValue<string>("TicketBaseUrl") ?? string.Empty;
        }

        public async Task<IActionResult> Index(int? deleted, string searchString)
        {
            if (_dbContext.DumpCallstacks != null)
            {
                var resultList = await FetchCallStacks(null, deleted, searchString);
                var dumpList = await FetchLastDumpFileInfos();
                var issueData = await GetIssueData(resultList);
                var data = new IndexPageData
                {
                    Callstacks = resultList,
                    UploadedDumps = dumpList,
                    ActiveFilterString = searchString,
                    IssueData = issueData
                };
                return View(data);
            }
            return View();
        }

        public async Task<IActionResult> Dump(int callstackId)
        {
            if (_dbContext.DumpCallstacks != null)
            {
                var list = await FetchCallStacks(callstackId, null);
                if (list != null && list.Count == 1)
                {
                    if (list[0].LinkedToDumpCallstackId != 0)
                    {
                        var linkedList = await FetchCallStacks(list[0].LinkedToDumpCallstackId, null);
                        if (linkedList != null && linkedList.Count == 1)
                        {
                            var issueData = await GetIssueData(linkedList);
                            var data = new DumpPageData
                            {
                                Callstack = linkedList[0],
                                IssueData = issueData.FirstOrDefault().Value
                            };
                            return View(data);
                        }
                    }
                    else
                    {
                        var issueData = await GetIssueData(list);
                        var data = new DumpPageData
                        {
                            Callstack = list[0],
                            IssueData = issueData.FirstOrDefault().Value
                        };
                        return View(data);
                    }
                }
            }
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        private async Task<Dictionary<string, IssueData>> GetIssueData(List<DumpCallstack> callstacks)
        {
            if (_issueTracker != null)
            {
                var issueIds = callstacks.Select(callstack => callstack.Ticket.Trim()).Distinct().ToList();
                issueIds.RemoveAll(string.IsNullOrEmpty);
                issueIds = issueIds.Distinct().ToList();

                var httpClient = _httpClientFactory.CreateClient();
                return await _issueTracker.GetIssueDataAsync(httpClient, issueIds, CancellationToken.None);
            }
            return new Dictionary<string, IssueData>();
        }

        private async Task<List<DumpFileInfo>> FetchLastDumpFileInfos()
        {
            if (_dbContext.DumpFileInfos != null)
            {
                var resultList = await _dbContext.DumpFileInfos.AsNoTracking()
                    .OrderByDescending(dumpFileInfo => dumpFileInfo.UploadDate)
                    .Take(20)
                    .ToListAsync();
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
                        catch (Exception)
                        {
                        }
                    }
                }
                return resultList;
            }
            return [];
        }

        private async Task<List<DumpCallstack>> FetchCallStacks(int? id, int? deleted, string searchString = "")
        {
            if (_dbContext.DumpCallstacks != null)
            {
                List<DumpCallstack>? list = null;
                DateTime cutoffDate = DateTime.Now.AddDays(-_daysBack);

                if (id == null)
                {
                    if (string.IsNullOrWhiteSpace(searchString))
                        list = await _dbContext.DumpCallstacks.AsNoTracking()
                            .Include(dumpCallstack => dumpCallstack.DumpInfos)
                            .Include(dumpCallstack => dumpCallstack.LogFileDatas)
                            .ThenInclude(logFileLine => logFileLine.DumpFileInfo)
                            .Where(dumpCallstack => dumpCallstack.Deleted == (deleted > 0) &&
                                                    (dumpCallstack.DumpInfos.Any(dumpInfo => dumpInfo.UploadDate >= cutoffDate) ||
                                                    dumpCallstack.LogFileDatas.Count != 0))
                            .ToListAsync();
                    else // with search string, include deleted callstacks
                        list = await _dbContext.DumpCallstacks.AsNoTracking()
                            .Include(dumpCallstack => dumpCallstack.DumpInfos)
                            .Include(dumpCallstack => dumpCallstack.LogFileDatas)
                            .ThenInclude(logFileLine => logFileLine.DumpFileInfo)
                            .ToListAsync();
                }
                else
                {
                    list = await _dbContext.DumpCallstacks.AsNoTracking()
                        .Include(dumpCallstack => dumpCallstack.DumpInfos)
                        .Include(dumpCallstack => dumpCallstack.LogFileDatas)
                        .ThenInclude(logFileLine => logFileLine.DumpFileInfo)
                        .Where(dumpCallstack => dumpCallstack.DumpCallstackId == id ||
                                                dumpCallstack.LinkedToDumpCallstackId == id)
                        .ToListAsync();
                }

                if (!string.IsNullOrWhiteSpace(searchString))
                {
                    // Convert wildcard search string to regex pattern
                    string pattern = ".*" + Regex.Escape(searchString).Replace("\\*", ".*").Replace("\\?", ".") + ".*";
                    Regex regex = new Regex(pattern, RegexOptions.IgnoreCase);

                    list = list.Where(dumpCallstack =>
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
                            regex.IsMatch(dumpFileInfo.Environment) ||
                            regex.IsMatch(dumpFileInfo.ComputerName) ||
                            regex.IsMatch(dumpFileInfo.Domain)))
                        .ToList();
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
                                logFileData.LineNumbers = logFileData.LineNumberString.Split(',').Select(long.Parse).ToList();
                        }
                    }
                }

                Dictionary<int, List<DumpCallstack>> groupedCallstacks = new();
                List<DumpCallstack> resultList = new();
                foreach (var callstack in list)
                {
                    if (callstack.LinkedToDumpCallstackId != 0)
                        continue;
                    groupedCallstacks[callstack.DumpCallstackId] = new List<DumpCallstack> { callstack };
                }
                foreach (var callstack in list)
                {
                    if (callstack.LinkedToDumpCallstackId == 0 || callstack.DumpCallstackId == callstack.LinkedToDumpCallstackId)
                        continue;
                    if (groupedCallstacks.ContainsKey(callstack.LinkedToDumpCallstackId))
                        groupedCallstacks[callstack.LinkedToDumpCallstackId].Add(callstack);
                    else
                        groupedCallstacks[callstack.DumpCallstackId] = new List<DumpCallstack> { callstack };
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
                        for (int i = 1; i < group.Value.Count; i++)
                        {
                            first.DumpInfos.AddRange(group.Value[i].DumpInfos);
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
                        }
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

                    // if a callstack is marked as fixed, move it to the end of the list
                    if (!string.IsNullOrEmpty(a.FixedVersion) && string.IsNullOrEmpty(b.FixedVersion))
                        return 1;
                    if (string.IsNullOrEmpty(a.FixedVersion) && !string.IsNullOrEmpty(b.FixedVersion))
                        return -1;

                    if (a.DumpInfos.Count > 0 && b.DumpInfos.Count > 0)
                    {
                        var uploadA = a.DumpInfos.Max(dumpInfo => dumpInfo.UploadDate);
                        var uploadB = b.DumpInfos.Max(dumpInfo => dumpInfo.UploadDate);
                        if (uploadA != uploadB)
                            return uploadB.CompareTo(uploadA); // sort by date of last upload, so it's easy to find the just uploaded ones
                    }

                    // sort by number of dumps - the more dumps with the same callstack the more urgent it is to fix
                    return b.DumpInfos.Count - a.DumpInfos.Count;
                });


                return resultList;
            }
            return new List<DumpCallstack>();
        }

        private bool HasDumpAfterFixedVersion(DumpCallstack dumpCallstack)
        {
            var appVersion = new SemanticVersion(dumpCallstack.ApplicationVersion, dumpCallstack.BuildType);
            var fixedVersion = new SemanticVersion(dumpCallstack.FixedVersion, dumpCallstack.FixedBuildType);
            return appVersion > fixedVersion;
        }
    }
}
