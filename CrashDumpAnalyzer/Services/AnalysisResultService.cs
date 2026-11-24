using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.Models;
using CrashDumpAnalyzer.Utilities;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace CrashDumpAnalyzer.Services
{
    public class AnalysisResultService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<AnalysisResultService> _logger;
        private readonly CallstackService _callstacks;
        private readonly string _dumpPath;
        private readonly string _agestoreExe;
        private readonly string _cachePath;
        private readonly long _maxCacheSize;

        public AnalysisResultService(ApplicationDbContext dbContext, IConfiguration configuration, ILogger<AnalysisResultService> logger, CallstackService callstacks)
        {
            _dbContext = dbContext;
            _logger = logger;
            _callstacks = callstacks;
            _dumpPath = configuration.GetValue<string>("DumpPath") ?? string.Empty;
            _agestoreExe = configuration.GetValue<string>("AgestoreExe") ?? string.Empty;
            _cachePath = configuration.GetValue<string>("CachePath") ?? string.Empty;
            _maxCacheSize = configuration.GetValue<long>("MaxCacheSize");
            if (_maxCacheSize == 0)
                _maxCacheSize = 30_000_000_000;
        }

        public async Task UpdateLogDataInDatabase(string dumpFilePath, string uploadedFromHostname, Dictionary<LogIssue, List<(DateTime date, long lineNumber)>> logData, CancellationToken token, bool reanalysis = false)
        {
            // find the highest version of all the entries in the log file
            var highestVersion = new SemanticVersion(0, 0, 0, 0, -1);
            DateTime latestDate = DateTime.MinValue;
            foreach (var logIssue in logData)
            {
                var ver1 = new SemanticVersion(logIssue.Key.ApplicationVersion, logIssue.Key.BuildType);
                if (ver1 > highestVersion)
                    highestVersion = ver1;
                var maxDate = logIssue.Value.Max(item => item.date);
                if (latestDate < maxDate)
                    latestDate = maxDate;
            }
            DumpFileInfo? entry = null;
            if (_dbContext.DumpFileInfos != null)
                entry = await _dbContext.DumpFileInfos.FirstOrDefaultAsync(x => x.FilePath == dumpFilePath, token);

            if (entry != null)
            {
                if (reanalysis)
                {
                    var previousLines = await _dbContext.Set<LogFileData>()
                        .Where(line => line.DumpFileInfo != null && line.DumpFileInfo.DumpFileInfoId == entry.DumpFileInfoId)
                        .ToListAsync(token);
                    _dbContext.RemoveRange(previousLines);
                }
                entry.ApplicationVersion = highestVersion.ToVersionString();
                entry.DumpTime = latestDate;
                entry.LogSummary = $"Log contains {logData.Count} issues";

                if (!string.IsNullOrEmpty(uploadedFromHostname))
                    entry.UploadedFromHostname = uploadedFromHostname;

                if (logData.Count == 0)
                {
                    if (_dbContext.DumpCallstacks != null)
                    {
                        var unassigned = await _dbContext.DumpCallstacks
                            .Include(dumpCallstack => dumpCallstack.DumpInfos.Where(info => info.DumpFileInfoId == entry.DumpFileInfoId))
                            .FirstOrDefaultAsync(x => x.ApplicationName == Constants.UnassignedDumpNames, token);
                        unassigned?.DumpInfos.Remove(entry);
                    }
                    entry.LogSummary = $"Log contains no issues, log file deleted";
                    // log file contains nothing important: delete the file
                    try
                    {
                        if (!string.IsNullOrEmpty(entry.FilePath))
                            System.IO.File.Delete(Path.Combine(_dumpPath, entry.FilePath));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error deleting dump file {path}", Path.Combine(_dumpPath, entry.FilePath));
                        throw;
                    }
                    entry.FilePath = string.Empty;
                    await _dbContext.SaveChangesAsync(token);
                }
            }
            var callstacksByText = new Dictionary<string, DumpCallstack>(StringComparer.Ordinal);
            foreach (var logIssue in logData)
            {
                token.ThrowIfCancellationRequested();
                if (!callstacksByText.TryGetValue(logIssue.Key.IssueTextClean, out var callstack))
                {
                    callstack = _dbContext.DumpCallstacks == null ? null : await _dbContext.DumpCallstacks
                        .FirstOrDefaultAsync(cs => cs.CleanCallstack == logIssue.Key.IssueTextClean, token);
                    if (callstack == null)
                    {
                        callstack = new DumpCallstack
                        {
                            Callstack = logIssue.Key.IssueText,
                            CleanCallstack = logIssue.Key.IssueTextClean,
                            ExceptionType = logIssue.Key.IssueType,
                            ApplicationName = logIssue.Key.ApplicationName,
                            ApplicationVersion = logIssue.Key.ApplicationVersion,
                            BuildType = logIssue.Key.BuildType
                        };
                        _dbContext.Add(callstack);
                    }
                    else
                    {
                        callstack.Deleted = false;
                        if (callstack.LinkedToDumpCallstackId != 0 && _dbContext.DumpCallstacks != null)
                        {
                            var linked = await _dbContext.DumpCallstacks
                                .FirstOrDefaultAsync(cs => cs.DumpCallstackId == callstack.LinkedToDumpCallstackId, token);
                            if (linked != null)
                                linked.Deleted = false;
                        }
                    }
                    callstacksByText.Add(logIssue.Key.IssueTextClean, callstack);
                }

                var issueVersion = new SemanticVersion(logIssue.Key.ApplicationVersion, logIssue.Key.BuildType);
                var callstackVersion = new SemanticVersion(callstack.ApplicationVersion, callstack.BuildType);
                if (issueVersion >= callstackVersion)
                {
                    callstack.ApplicationVersion = logIssue.Key.ApplicationVersion;
                    callstack.BuildType = logIssue.Key.BuildType;
                }

                if (logIssue.Value.Count > 0)
                {
                    var lineNumbers = logIssue.Value.Select(item => item.lineNumber).ToList();
                    callstack.LogFileDatas.Add(new LogFileData
                    {
                        DumpFileInfo = entry,
                        LatestTime = logIssue.Value.Max(item => item.date),
                        LineNumbers = lineNumbers,
                        LineNumberString = string.Join(",", lineNumbers)
                    });
                }
                if (entry != null)
                    entry.DumpCallstack = callstack;
            }

            var outdated = new List<DumpCallstack>();
            foreach (var callstack in callstacksByText.Values)
            {
                var version = new SemanticVersion(callstack.ApplicationVersion, callstack.BuildType);
                if (!version.isValid() || string.IsNullOrEmpty(callstack.ApplicationName))
                    continue;
                if (MinVersions.IsVersionSupported(callstack.ApplicationName, version))
                {
                    if (callstack.Comment.Contains(CallstackComments.VersionTooOld, StringComparison.InvariantCultureIgnoreCase))
                    {
                        CallstackComments.Remove(callstack, CallstackComments.VersionTooOld);
                        callstack.Deleted = false;
                    }
                }
                else
                {
                    _logger.LogInformation("deleting callstack because it's too old. {ApplicationName} - {Version}", callstack.ApplicationName, version.ToVersionString());
                    outdated.Add(callstack);
                }
            }

            if (logData.Count > 0)
                await _dbContext.SaveChangesAsync(token);
            foreach (var callstack in outdated)
            {
                token.ThrowIfCancellationRequested();
                await _callstacks.DeleteDumpCallstack(callstack.DumpCallstackId, CallstackComments.VersionTooOld, false);
            }
        }


        public async Task UpdateDumpDataInDatabase(string dumpFilePath, string? uploadedFromHostname, DumpData dumpData, int? callstackId, CancellationToken token)
        {
            // we have the call stack, now update the database
            DumpFileInfo? entry = null;
            if (_dbContext.DumpFileInfos != null)
                entry = await _dbContext.DumpFileInfos.FirstOrDefaultAsync(x => x.FilePath == dumpFilePath, token);

            if (entry != null)
            {
                entry.CallStack = dumpData.callstackString;
                entry.ApplicationName = dumpData.processName;
                entry.ExceptionType = dumpData.exceptionCode;
                entry.ApplicationVersion = dumpData.version;
                entry.DumpTime = dumpData.dumpTime;
                entry.DumpTimeOffset = dumpData.dumpTimeOffset;
                if (!string.IsNullOrEmpty(uploadedFromHostname))
                    entry.UploadedFromHostname = uploadedFromHostname;
                entry.ComputerName = dumpData.computerName;
                entry.Domain = dumpData.domain;
                entry.Environment = dumpData.environment;
                entry.VersionResource = dumpData.versionResource;
                entry.CommandLine = dumpData.commandLine;
                entry.Diagnostics = dumpData.diagnostics;

                // find out if we already have this callstack
                DumpCallstack callstack = new DumpCallstack
                {
                    Callstack = dumpData.callstackString,
                    CleanCallstack = dumpData.cleanCallstackString,
                    ApplicationName = dumpData.processName,
                    ExceptionType = dumpData.exceptionCode,
                    ApplicationVersion = dumpData.version,
                    BuildType = BuildTypes.ExtractBuildType(dumpData.versionResource)
                };
                bool doUpdate = false;
                if (_dbContext.DumpCallstacks != null)
                {
                    try
                    {
                        var cs = await _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstOrDefaultAsync(
                            x => x.CleanCallstack == dumpData.cleanCallstackString, token);

                        // 80000003 is a breakpoint exception, not a crash.
                        // This always gives us the callstack of the main thread, which is the same for most dumps that are created because of a hang.
                        // so we never attempt to assign these to an existing callstack
                        if (cs != null && !dumpData.exceptionCode.StartsWith("80000003") && cs.ApplicationName != Constants.UnassignedDumpNames)
                        {
                            callstack = cs;
                            callstack.Deleted = false;
                            var v1 = new SemanticVersion(dumpData.version, BuildTypes.ExtractBuildType(dumpData.versionResource));
                            var v2 = new SemanticVersion(callstack.ApplicationVersion, callstack.BuildType);
                            if (v1 >= v2)
                            {
                                callstack.ApplicationVersion = dumpData.version;
                                callstack.BuildType = BuildTypes.ExtractBuildType(dumpData.versionResource);
                            }
                            doUpdate = true;

                            // if this dump is linked to another callstack, we need to ensure the linked callstack is not deleted
                            if (cs.LinkedToDumpCallstackId != 0)
                            {
                                var linked = await _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstOrDefaultAsync(x => x.DumpCallstackId == cs.LinkedToDumpCallstackId, token);
                                if (linked != null)
                                {
                                    linked.Deleted = false;
                                    _dbContext.Update(linked);
                                }
                            }
                            if (callstackId != null && cs.DumpCallstackId != callstackId)
                            {
                                // remove the re-checked dump from the original callstack
                                var origCallstack = await _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstOrDefaultAsync(
                                                                                               x => x.DumpCallstackId == callstackId, token);
                                if (origCallstack != null)
                                {
                                    origCallstack.DumpInfos.Remove(entry);
                                    if (origCallstack.DumpInfos.Count == 0)
                                    {
                                        // no more dump files in this callstack => delete it
                                        origCallstack.Deleted = true;
                                        _dbContext.Update(origCallstack);
                                    }
                                }
                            }
                        }
                        else if (callstackId != null)
                        {
                            // no existing callstack found for this dump, use the original one and update that
                            var origCallstack = await _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstOrDefaultAsync(
                                                                                           x => x.DumpCallstackId == callstackId, token);
                            if (origCallstack != null && origCallstack.DumpInfos.Count > 1)
                            {
                                origCallstack.DumpInfos.Remove(entry);
                            }
                            else if (origCallstack != null)
                            {
                                callstack = origCallstack;
                                callstack.Deleted = false;
                                var v1 = new SemanticVersion(dumpData.version, BuildTypes.ExtractBuildType(dumpData.versionResource));
                                var v2 = new SemanticVersion(callstack.ApplicationVersion, callstack.BuildType);
                                if (v1 >= v2)
                                {
                                    callstack.ApplicationVersion = dumpData.version;
                                    callstack.BuildType = BuildTypes.ExtractBuildType(dumpData.versionResource);
                                }
                                origCallstack.CleanCallstack = dumpData.cleanCallstackString;
                                origCallstack.Callstack = dumpData.callstackString;
                                doUpdate = true;
                            }
                        }

                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error parsing callstack");
                        throw;
                    }
                    if (callstackId == null)
                    {
                        var unassigned = await _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstOrDefaultAsync(
                                                                                       x => x.ApplicationName == Constants.UnassignedDumpNames, token);
                        unassigned?.DumpInfos.Remove(entry);
                    }
                }
                callstack.DumpInfos.Add(entry);
                if (doUpdate)
                    _dbContext.Update(callstack);
                else
                    _dbContext.Add(callstack);
                await _dbContext.SaveChangesAsync(token);
                // if the callstack has a version that's not supported anymore, delete it
                if (callstack.ApplicationVersion != null)
                {
                    var version = new SemanticVersion(callstack.ApplicationVersion, callstack.BuildType);
                    if (version.isValid() && !string.IsNullOrEmpty(callstack.ApplicationName))
                    {
                        if (MinVersions.IsVersionSupported(callstack.ApplicationName, version))
                        {
                            if (callstack.Comment.Contains(CallstackComments.VersionTooOld, StringComparison.InvariantCultureIgnoreCase))
                            {
                                CallstackComments.Remove(callstack, CallstackComments.VersionTooOld);
                                callstack.Deleted = false;
                                await _dbContext.SaveChangesAsync(token);
                            }
                        }
                        else
                        {
                            await _callstacks.DeleteDumpCallstack(callstack.DumpCallstackId, CallstackComments.VersionTooOld, false);
                        }
                    }
                }
                // now run agestore to keep the cache size in check
                if (_agestoreExe != string.Empty)
                {
                    using Process agestoreProcess = new();
                    agestoreProcess.StartInfo.FileName = _agestoreExe;
                    agestoreProcess.StartInfo.WorkingDirectory = Path.GetDirectoryName(_agestoreExe);
                    agestoreProcess.StartInfo.Arguments = $"{_cachePath} -size={_maxCacheSize} -s -y";
                    agestoreProcess.StartInfo.RedirectStandardOutput = true;
                    agestoreProcess.Start();
                    StreamReader agestoreSr = agestoreProcess.StandardOutput;
                    string agestoreOutput = await agestoreSr.ReadToEndAsync(token);
                    await agestoreProcess.WaitForExitAsync(token);
                    _logger.LogInformation("agestore output: {out}", agestoreOutput);
                }
            }
        }
    }
}
