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

namespace CrashDumpAnalyzer.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _dbContext;
        private readonly int _daysBack;

        public HomeController(IConfiguration configuration, ILogger<HomeController> logger, ApplicationDbContext context)
        {
            _logger = logger;
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
                var data = new IndexPageData
                {
                    Callstacks = resultList,
                    UploadedDumps = dumpList,
                    ActiveFilterString = searchString
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
                            return View(linkedList[0]);
                    }
                    else
                    {
                        return View(list[0]);
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
                        list = await _dbContext.DumpCallstacks.AsNoTracking().Include(dumpCallstack => dumpCallstack.DumpInfos)
                            .Where(dumpCallstack => dumpCallstack.Deleted == (deleted > 0) &&
                                                    dumpCallstack.DumpInfos.Any(dumpInfo => dumpInfo.UploadDate >= cutoffDate))
                            .ToListAsync();
                    else // with search string, include deleted callstacks
                        list = await _dbContext.DumpCallstacks.AsNoTracking().Include(dumpCallstack => dumpCallstack.DumpInfos)
                            .ToListAsync();
                }
                else
                {
                    list = await _dbContext.DumpCallstacks.AsNoTracking().Include(dumpCallstack => dumpCallstack.DumpInfos)
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

                // sort individual dumps by upload date
                foreach (var dumpCallstack in list)
                {
                    if (dumpCallstack.DumpInfos.Count > 0)
                        dumpCallstack.DumpInfos.Sort((a, b) => b.UploadDate.CompareTo(a.UploadDate));
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
                            if (first.FixedVersion != group.Value[i].FixedVersion)
                            {
                                if (first.FixedVersion.Length == 0)
                                    first.FixedVersion = group.Value[i].FixedVersion;
                                if (group.Value[i].FixedVersion.Length != 0)
                                {
                                    var firstVersion = new SemanticVersion(first.FixedVersion);
                                    var secondVersion = new SemanticVersion(group.Value[i].FixedVersion);
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
                                    var firstVersion = new SemanticVersion(first.ApplicationVersion);
                                    var secondVersion = new SemanticVersion(group.Value[i].ApplicationVersion);
                                    first.ApplicationVersion = firstVersion > secondVersion ? first.ApplicationVersion : group.Value[i].ApplicationVersion;
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
            var appVersion = new SemanticVersion(dumpCallstack.ApplicationVersion);
            var fixedVersion = new SemanticVersion(dumpCallstack.FixedVersion);
            return appVersion > fixedVersion;
        }
    }
}
