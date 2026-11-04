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

        public async Task UpdateLogDataInDatabase(string dumpFilePath, string uploadedFromHostname, Dictionary<LogIssue, List<(DateTime date, long lineNumber)>> logData, CancellationToken token)
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
                entry.ApplicationVersion = highestVersion.ToVersionString();
                entry.DumpTime = latestDate;
                entry.LogSummary = $"Log contains {logData.Count} issues";

                if (!string.IsNullOrEmpty(uploadedFromHostname))
                    entry.UploadedFromHostname = uploadedFromHostname;

                if (_dbContext.DumpCallstacks != null)
                {
                    var unassigned = await _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos)
                        .FirstOrDefaultAsync(
                            x => x.ApplicationName == Constants.UnassignedDumpNames, token);
                    unassigned?.DumpInfos.Remove(entry);
                }
                if (logData.Count == 0)
                {
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
            foreach (var logIssue in logData)
            {
                var callstack = new DumpCallstack
                {
                    Callstack = logIssue.Key.IssueText,
                    CleanCallstack = logIssue.Key.IssueTextClean,
                    ExceptionType = logIssue.Key.IssueType,
                    ApplicationName = logIssue.Key.ApplicationName,
                    ApplicationVersion = logIssue.Key.ApplicationVersion,
                    BuildType = logIssue.Key.BuildType,
                    LogFileDatas = []
                };
                if (logIssue.Value.Count > 0)
                {
                    var logFileLine = new LogFileData
                    {
                        DumpFileInfo = entry,
                        LatestTime = DateTime.MinValue,
                        LineNumberString = string.Empty,
                        LineNumbers = []
                    };
                    callstack.LogFileDatas.Add(logFileLine);

                    foreach (var (date, lineNumber) in logIssue.Value)
                    {
                        logFileLine.LineNumbers.Add(lineNumber);
                        logFileLine.LatestTime = logFileLine.LatestTime < date ? date : logFileLine.LatestTime;
                    }
                    logFileLine.LineNumberString = string.Join(",", logFileLine.LineNumbers);
                }
                bool doUpdate = false;
                if (_dbContext.DumpCallstacks != null)
                {
                    try
                    {
                        var cs = await _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.LogFileDatas).ThenInclude(logFileLine => logFileLine.DumpFileInfo)
                            .FirstOrDefaultAsync(
                            x => x.CleanCallstack == logIssue.Key.IssueTextClean, token);

                        if (cs != null)
                        {
                            callstack = cs;
                            callstack.Deleted = false;
                            var v1 = new SemanticVersion(logIssue.Key.ApplicationVersion, logIssue.Key.BuildType);
                            var v2 = new SemanticVersion(callstack.ApplicationVersion, callstack.BuildType);
                            if (v1 >= v2)
                            {
                                callstack.ApplicationVersion = logIssue.Key.ApplicationVersion;
                                callstack.BuildType = logIssue.Key.BuildType;
                            }

                            if (logIssue.Value.Count > 0)
                            {
                                var logFileLine = new LogFileData
                                {
                                    DumpFileInfo = entry,
                                    LatestTime = DateTime.MinValue,
                                    LineNumberString = string.Empty,
                                    LineNumbers = []
                                };
                                callstack.LogFileDatas.Add(logFileLine);

                                foreach (var (date, lineNumber) in logIssue.Value)
                                {
                                    logFileLine.LineNumbers.Add(lineNumber);
                                    logFileLine.LatestTime = logFileLine.LatestTime < date ? date : logFileLine.LatestTime;
                                }
                                logFileLine.LineNumberString = string.Join(",", logFileLine.LineNumbers);
                            }

                            doUpdate = true;

                            // if this dump is linked to another callstack, we need to ensure the linked callstack is not deleted
                            if (cs.LinkedToDumpCallstackId != 0)
                            {
                                var linked = await _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.LogFileDatas).ThenInclude(logFileLine => logFileLine.DumpFileInfo)
                                    .FirstOrDefaultAsync(x => x.DumpCallstackId == cs.LinkedToDumpCallstackId, token);
                                if (linked != null)
                                {
                                    linked.Deleted = false;
                                    _dbContext.Update(linked);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error parsing logfile");
                        throw;
                    }
                }
                if (entry != null)
                    callstack.DumpInfos.Add(entry);

                if (doUpdate)
                    _dbContext.Update(callstack);
                else
                    _dbContext.Add(callstack);

                await _dbContext.SaveChangesAsync(token);
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
                            _logger.LogInformation("deleting callstack because it's too old. {ApplicationName} - {Version}", callstack.ApplicationName, version.ToVersionString());
                            await _callstacks.DeleteDumpCallstack(callstack.DumpCallstackId, CallstackComments.VersionTooOld, false);
                        }
                    }
                }
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
                            if (origCallstack != null)
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
