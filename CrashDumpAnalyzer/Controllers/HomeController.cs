using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.Models;

using Microsoft.AspNetCore.Mvc;

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using CrashDumpAnalyzer.Utilities;

namespace CrashDumpAnalyzer.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _dbContext;

        public HomeController(ILogger<HomeController> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _dbContext = context;
        }

        public async Task<IActionResult> Index()
        {
            if (_dbContext.DumpCallstacks != null)
            {
                var list = await _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).ToListAsync();
                list.Sort((a, b) =>
                {
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
                    // next sort by number of dumps - the more dumps with the same callstack the more urgent it is to fix
                    if (a.DumpInfos.Count != b.DumpInfos.Count)
                        return a.DumpInfos.Count - b.DumpInfos.Count;
                    // finally sort by date of last dump
                    return b.DumpInfos.Max(dumpInfo => dumpInfo.UploadDate).CompareTo(a.DumpInfos.Max(dumpInfo => dumpInfo.UploadDate));
                });
                return View(list);
            }            return View();
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

        private bool HasDumpAfterFixedVersion(DumpCallstack dumpCallstack)
        {
            var appVersion = new SemanticVersion(dumpCallstack.ApplicationVersion);
            var fixedVersion = new SemanticVersion(dumpCallstack.FixedVersion);
            return appVersion > fixedVersion;
        }
    }
}
