using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.WebUtilities;
using System.Net;
using CrashDumpAnalyzer.Utilities;
using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.Models;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace CrashDumpAnalyzer.Controllers
{

    public class ApiController : Controller
    {
        private const long MaxFileSize = long.MaxValue;
        private readonly string[] _permittedExtensions = [".dmp", ".dump"];
        private readonly ILogger<ApiController> _logger;
        private readonly ApplicationDbContext _dbContext;
        private readonly IBackgroundTaskQueue _queue;
        private readonly IServiceProvider _provider;
        private readonly string _dumpPath;
        private readonly string _cdbExe;
        private readonly string _agestoreExe;
        private readonly string _cachePath;
        private readonly string _symbolPath;
        private readonly long _maxCacheSize;
        private readonly long _deleteDumpsUploadedBeforeDays;

        public ApiController(IConfiguration configuration, ILogger<ApiController> logger,
                            IServiceProvider provider,
                            ApplicationDbContext dbContext,
                            IBackgroundTaskQueue queue)
        {
            this._logger = logger;
            this._dbContext = dbContext;
            this._queue = queue;
            this._provider = provider;
            Debug.Assert(_dbContext != null);
            Debug.Assert(_queue != null);
            Debug.Assert(_logger != null);

            this._dumpPath = configuration.GetValue<string>("DumpPath") ?? string.Empty;
            this._cdbExe = configuration.GetValue<string>("CdbExe") ?? "cdb.exe";
            this._symbolPath = configuration.GetValue<string>("SymbolPath") ?? string.Empty;
            this._cachePath = configuration.GetValue<string>("CachePath") ?? string.Empty;
            this._agestoreExe = configuration.GetValue<string>("AgestoreExe") ?? string.Empty;
            this._maxCacheSize = configuration.GetValue<long>("MaxCacheSize");
            if (this._maxCacheSize == 0)
                this._maxCacheSize = 30_000_000_000;
            this._deleteDumpsUploadedBeforeDays = configuration.GetValue<long>("DeleteDumpsUploadedBeforeDays");
            if (this._deleteDumpsUploadedBeforeDays == 0)
                this._deleteDumpsUploadedBeforeDays = 30;
        }


        [HttpPost]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSize)]
        public async Task<IActionResult> UploadFiles()
        {
            if (Request.ContentType != null && !MultipartRequestHelper.IsMultipartContentType(Request.ContentType))
            {
                ModelState.AddModelError("File",
                    "The request couldn't be processed (Error 1).");
                // Log error

                return BadRequest(ModelState);
            }

            string boundary = MultipartRequestHelper.GetBoundary(
                MediaTypeHeaderValue.Parse(Request.ContentType));
            MultipartReader reader = new MultipartReader(boundary, HttpContext.Request.Body);
            MultipartSection? section = await reader.ReadNextSectionAsync();
            string uploadedFromIp = string.Empty;
            if (Request.HttpContext.Connection.RemoteIpAddress != null)
                uploadedFromIp = Request.HttpContext.Connection.RemoteIpAddress.ToString();

            while (section != null)
            {
                bool hasContentDispositionHeader =
                    ContentDispositionHeaderValue.TryParse(
                        section.ContentDisposition, out ContentDispositionHeaderValue? contentDisposition);
                if (hasContentDispositionHeader)
                {
                    // This check assumes that there's a file
                    // present without form data. If form data
                    // is present, this method immediately fails
                    // and returns the model error.
                    if (contentDisposition != null && !MultipartRequestHelper
                            .HasFileContentDisposition(contentDisposition))
                    {
                        ModelState.AddModelError("File",
                            "The request couldn't be processed (Error 2).");
                        // Log error

                        return BadRequest(ModelState);
                    }
                    // Don't trust the file name sent by the client. To display
                    // the file name, HTML-encode the value.

                    if (contentDisposition != null)
                    {
                        string? trustedFileNameForDisplay = WebUtility.HtmlEncode(
                            contentDisposition.FileName.Value);
                        string trustedFileNameForFileStorage = DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetRandomFileName() + ".dmp";
                        await using (FileStream targetStream = System.IO.File.Create(
                                         Path.Combine(_dumpPath, trustedFileNameForFileStorage), 10_000_000))
                        {
                            var success = await FileHelpers.ProcessStreamedFile(
                                section, contentDisposition, ModelState,
                                _permittedExtensions, MaxFileSize, targetStream);

                            if (!ModelState.IsValid || !success)
                            {
                                targetStream.Close();
                                System.IO.File.Delete(Path.Combine(_dumpPath, trustedFileNameForFileStorage));
                                return BadRequest(ModelState);
                            }

                            DumpFileInfo entry = new DumpFileInfo
                            {
                                FilePath = trustedFileNameForFileStorage,
                                FileSize = targetStream.Length,
                                UploadDate = DateTime.Now,
                                UploadedFromIp = uploadedFromIp,
                            };

                            DumpCallstack callstack = new DumpCallstack
                            {
                                Callstack = string.Empty,
                                ApplicationName = Constants.UnassignedDumpNames,
                                ExceptionType = string.Empty,
                                ApplicationVersion = string.Empty
                            };
                            bool doUpdate = false;
                            if (_dbContext.DumpCallstacks != null)
                            {
                                var cs = await _dbContext.DumpCallstacks.FirstOrDefaultAsync(
                                    x => x.ApplicationName == Constants.UnassignedDumpNames);
                                if (cs != null)
                                {
                                    callstack = cs;
                                    callstack.Deleted = false;
                                    callstack.FixedVersion = string.Empty;
                                    callstack.FixedBuildType = -1;
                                    callstack.Ticket = string.Empty;
                                    callstack.Comment = string.Empty;
                                    doUpdate = true;
                                }
                            }

                            callstack.DumpInfos.Add(entry);
                            if (doUpdate)
                                _dbContext.Update(callstack);
                            else
                                _dbContext.Add(callstack);

                            _logger.LogInformation(
                                "Uploaded file '{TrustedFileNameForDisplay}' saved to " +
                                "'{TargetFilePath}' as {TrustedFileNameForFileStorage}",
                                trustedFileNameForDisplay, _dumpPath,
                                trustedFileNameForFileStorage);
                        }
                        try
                        {
                            await _dbContext.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error saving file to database");
                            return BadRequest(ModelState);

                        }
                        var serviceScope = _provider.GetService<IServiceScopeFactory>()?.CreateScope();
                        var dbContext = serviceScope?.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        if (dbContext == null)
                        {
                            _logger.LogError("Error getting ApplicationDbContext");
                            return BadRequest(ModelState);
                        }
                        _ = _queue.QueueBackgroundWorkItemAsync(async (token) =>
                        {
                            // use cbg to get a callstack from the dump
                            // and then update the database with the callstack
                            string dumpFilePath = Path.Combine(_dumpPath, trustedFileNameForFileStorage);
                            var dumpAnalyzer = new DumpAnalyzer(_cdbExe, _symbolPath, _logger);
                            var dumpAnalyzeTask = dumpAnalyzer.AnalyzeDump(dumpFilePath, token);

                            string uploadedFromHostname = "unknown host";
                            try
                            {
                                // while the process analyzes the dump, we fetch the computer name from the ip
                                var myIp = IPAddress.Parse(uploadedFromIp);
                                var getIpHost = await Dns.GetHostEntryAsync(myIp);
                                List<string> compName = [.. getIpHost.HostName.Split('.')];
                                uploadedFromHostname = compName.First();
                            }
                            catch
                            {
                                // ignored
                            }

                            await CleanupTask(dbContext, token);
                            var dumpData = await dumpAnalyzeTask;
                            await UpdateDumpDataInDatabase(dbContext, trustedFileNameForFileStorage, uploadedFromHostname, dumpData, null, token);
                        });

                    }
                }
                GC.Collect();
                // Drain any remaining section body that hasn't been consumed and
                // read the headers for the next section.
                section = await reader.ReadNextSectionAsync();
            }

            return Created(nameof(ApiController), null);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteDumpCallstack(int id)
        {
            if (_dbContext.DumpCallstacks == null)
                return NotFound();
            var dumpCallstack = await _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstAsync(cs => cs.DumpCallstackId == id);
            if (dumpCallstack.DumpCallstackId != id)
                return NotFound();

            try
            {
                // only mark the callstack as deleted, do not delete it from the db
                dumpCallstack.Deleted = true;
                // but we delete all dump files from this callstack:
                // the callstack itself is still stored as text in the db
                foreach (var dumpInfo in dumpCallstack.DumpInfos)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(dumpInfo.FilePath))
                            System.IO.File.Delete(Path.Combine(_dumpPath, dumpInfo.FilePath));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error deleting dump file {Path.Combine(_dumpPath, dumpInfo.FilePath)}");
                    }
                    dumpInfo.FilePath = string.Empty;
                }
                await _dbContext.SaveChangesAsync();
                try
                {
                    var dumpCallstacks = _dbContext.DumpCallstacks.Include(dmpCallstack => dmpCallstack.DumpInfos).Where(cs => cs.LinkedToDumpCallstackId == id);
                    foreach (var callStack in dumpCallstacks)
                    {
                        callStack.Deleted = true;
                        foreach (var dumpInfo in callStack.DumpInfos)
                        {
                            try
                            {
                                if (!string.IsNullOrEmpty(dumpInfo.FilePath))
                                    System.IO.File.Delete(Path.Combine(_dumpPath, dumpInfo.FilePath));
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error deleting dump file {Path.Combine(_dumpPath, dumpInfo.FilePath)}");
                            }
                            dumpInfo.FilePath = string.Empty;
                        }
                        await _dbContext.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deleting callstack from database");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting callstack from database");
            }
            return NoContent();
        }
        [HttpPost]
        public async Task<IActionResult> UnlinkDumpCallstack(int id)
        {
            if (_dbContext.DumpCallstacks == null)
                return NotFound();
            var dumpCallstack = await _dbContext.DumpCallstacks.FirstAsync(cs => cs.LinkedToDumpCallstackId == id);
            do
            {
                try
                {
                    dumpCallstack.LinkedToDumpCallstackId = 0;
                    await _dbContext.SaveChangesAsync();
                    dumpCallstack = await _dbContext.DumpCallstacks.FirstOrDefaultAsync(cs => cs.LinkedToDumpCallstackId == id);
                }
                catch (Exception ex)
                {
                    dumpCallstack = null;
                    _logger.LogError(ex, "Error unlinking");
                }
            } while (dumpCallstack != null);

            return NoContent();
        }

        [HttpPost]
        public async Task<IActionResult> DeleteDumpFile(int callstackId, int dumpId)
        {
            if (_dbContext.DumpCallstacks == null)
                return NotFound();

            try
            {
                var dumpCallstack = await _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos)
                    .FirstAsync(cs => (cs.DumpCallstackId == callstackId || cs.LinkedToDumpCallstackId == callstackId) && cs.DumpInfos.FirstOrDefault(x => x.DumpFileInfoId == dumpId) != null);
                var dumpToRemove = dumpCallstack.DumpInfos.FirstOrDefault(x => x.DumpFileInfoId == dumpId);
                if (dumpToRemove != null)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(dumpToRemove.FilePath))
                            System.IO.File.Delete(Path.Combine(_dumpPath, dumpToRemove.FilePath));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error deleting dump file {Path.Combine(_dumpPath, dumpToRemove.FilePath)}");
                    }
                    dumpToRemove.FilePath = string.Empty;
                    await _dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting callstack from database");
            }
            return NoContent();
        }

        [HttpGet]
        public async Task<IActionResult> DownloadFile(int id)
        {
            if (_dbContext.DumpFileInfos == null)
                return NotFound();
            var entry = await _dbContext.DumpFileInfos.FirstOrDefaultAsync(x => x.DumpFileInfoId == id);
            if (entry == null)
                return NotFound();
            var fs = new FileStream(Path.Combine(_dumpPath, entry.FilePath), FileMode.Open);

            // Return the file. A byte array can also be used instead of a stream
            return File(fs, "application/octet-stream", "Dump.dmp");
        }

        [HttpPost]
        public async Task<IActionResult> SetFixedVersion(int id, string? version, string? buildType)
        {
            if (_dbContext.DumpCallstacks == null)
                return NotFound();
            var entry = await _dbContext.DumpCallstacks.FirstOrDefaultAsync(x => x.DumpCallstackId == id);
            if (entry != null)
            {
                entry.FixedVersion = version ?? string.Empty;
                entry.FixedBuildType = buildType != null ? BuildTypes.ParseBuildType(buildType) : -1;
                var linkedList = await _dbContext.DumpCallstacks.Where(x => x.LinkedToDumpCallstackId == id).ToListAsync();
                foreach (var linked in linkedList)
                {
                    linked.FixedVersion = string.Empty;
                    linked.FixedBuildType = -1;
                }
                await _dbContext.SaveChangesAsync();
                ModelState.Clear();
            }
            return NoContent();
        }
        [HttpPost]
        public async Task<IActionResult> SetTicket(int id, string ticket)
        {
            if (_dbContext.DumpCallstacks == null)
                return NotFound();
            var entry = await _dbContext.DumpCallstacks.FirstOrDefaultAsync(x => x.DumpCallstackId == id);
            if (entry != null)
            {
                entry.Ticket = ticket;
                var linkedList = await _dbContext.DumpCallstacks.Where(x => x.LinkedToDumpCallstackId == id).ToListAsync();
                foreach (var linked in linkedList)
                {
                    linked.Ticket = string.Empty;
                }
                await _dbContext.SaveChangesAsync();
                ModelState.Clear();
            }
            return NoContent();
        }
        [HttpPost]
        public async Task<IActionResult> SetComment(int id, string comment)
        {
            _logger.LogInformation("Setting comment for {id} to {comment}", id, comment);
            if (_dbContext.DumpCallstacks == null)
                return NotFound();
            var entry = await _dbContext.DumpCallstacks.FirstOrDefaultAsync(x => x.DumpCallstackId == id);
            if (entry != null)
            {
                entry.Comment = comment;
                var linkedList = await _dbContext.DumpCallstacks.Where(x => x.LinkedToDumpCallstackId == id).ToListAsync();
                foreach (var linked in linkedList)
                {
                    linked.Comment = string.Empty;
                }
                await _dbContext.SaveChangesAsync();
                ModelState.Clear();
            }
            return NoContent();
        }

        [HttpPost]
        public async Task<IActionResult> LinkCallstack(int id, int toId)
        {
            if (_dbContext.DumpCallstacks == null)
                return NotFound();
            if (id == toId)
                return BadRequest();
            var entry = await _dbContext.DumpCallstacks.FirstOrDefaultAsync(x => x.DumpCallstackId == id);
            if (entry != null)
            {
                entry.LinkedToDumpCallstackId = toId;
                await _dbContext.SaveChangesAsync();
                ModelState.Clear();

                do
                {
                    entry = await _dbContext.DumpCallstacks.FirstOrDefaultAsync(x => x.LinkedToDumpCallstackId == id);
                    if (entry != null)
                    {
                        entry.LinkedToDumpCallstackId = toId;
                        await _dbContext.SaveChangesAsync();
                    }
                } while (entry != null);


            }
            return NoContent();
        }

        public async Task<IActionResult> ReAnalyzeDumpFile(int callstackId)
        {
            if (_dbContext.DumpCallstacks == null)
                return NotFound();
            DumpCallstack? dumpCallstack = await _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos)
                .FirstOrDefaultAsync(cs => (cs.DumpCallstackId == callstackId || cs.LinkedToDumpCallstackId == callstackId));
            if (dumpCallstack == null)
                return NotFound();
            try
            {
                var dumpToAnalyze = dumpCallstack.DumpInfos.FirstOrDefault(x => !string.IsNullOrEmpty(x.FilePath));
                if (dumpToAnalyze != null)
                {
                    if (string.IsNullOrEmpty(dumpToAnalyze.FilePath))
                        return NotFound();

                    var serviceScope = _provider.GetService<IServiceScopeFactory>()?.CreateScope();
                    var dbContext = serviceScope?.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    if (dbContext == null)
                    {
                        _logger.LogError("Error getting ApplicationDbContext");
                        return BadRequest(ModelState);
                    }
                    _ = _queue.QueueBackgroundWorkItemAsync(async (token) =>
                    {
                        try
                        {
                            var dumpAnalyzer = new DumpAnalyzer(_cdbExe, _symbolPath, _logger);
                            var dumpData = await dumpAnalyzer.AnalyzeDump(Path.Combine(_dumpPath, dumpToAnalyze.FilePath), token);
                            await UpdateDumpDataInDatabase(dbContext, dumpToAnalyze.FilePath, null, dumpData, dumpCallstack.ApplicationName == Constants.UnassignedDumpNames ? null : callstackId, token);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error re-analyzing callstack");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error re-analyzing callstack");
            }
            return NoContent();
        }
        private async Task UpdateDumpDataInDatabase(ApplicationDbContext dbContext, string dumpFilePath, string? uploadedFromHostname, DumpData dumpData, int? callstackId, CancellationToken token)
        {
            // we have the call stack, now update the database
            DumpFileInfo? entry = null;
            if (dbContext.DumpFileInfos != null)
                entry = await dbContext.DumpFileInfos.FirstOrDefaultAsync(x => x.FilePath == dumpFilePath, token);

            if (entry != null)
            {
                entry.CallStack = dumpData.callstackString;
                entry.ApplicationName = dumpData.processName;
                entry.ExceptionType = dumpData.exceptionCode;
                entry.ApplicationVersion = dumpData.version;
                entry.DumpTime = dumpData.dumpTime;
                if (!string.IsNullOrEmpty(uploadedFromHostname))
                    entry.UploadedFromHostname = uploadedFromHostname;
                entry.ComputerName = dumpData.computerName;
                entry.Domain = dumpData.domain;
                entry.Environment = dumpData.environment;
                entry.VersionResource = dumpData.versionResource;

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
                if (dbContext.DumpCallstacks != null)
                {
                    try
                    {
                        var cs = await dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstOrDefaultAsync(
                            x => x.CleanCallstack == dumpData.cleanCallstackString && x.ApplicationName == dumpData.processName, token);

                        // 80000003 is a breakpoint exception, not a crash.
                        // This always gives us the callstack of the main thread, which is the same for most dumps that are created because of a hang.
                        // so we never attempt to assign these to an existing callstack
                        if (cs != null && !dumpData.exceptionCode.StartsWith("80000003"))
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
                                var linked = await dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstOrDefaultAsync(x => x.DumpCallstackId == cs.LinkedToDumpCallstackId, token);
                                if (linked != null)
                                {
                                    linked.Deleted = false;
                                    dbContext.Update(linked);
                                }
                            }
                            if (callstackId != null && cs.DumpCallstackId != callstackId)
                            {
                                // remove the re-checked dump from the original callstack
                                var origCallstack = await dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstOrDefaultAsync(
                                                                                               x => x.DumpCallstackId == callstackId, token);
                                if (origCallstack != null)
                                {
                                    origCallstack.DumpInfos.Remove(entry);
                                    if (origCallstack.DumpInfos.Count == 0)
                                    {
                                        // no more dump files in this callstack => delete it
                                        origCallstack.Deleted = true;
                                        dbContext.Update(origCallstack);
                                    }
                                }
                            }
                        }
                        else if (callstackId != null)
                        {
                            // no existing callstack found for this dump, use the original one and update that
                            var origCallstack = await dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstOrDefaultAsync(
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
                    }
                    if (callstackId == null)
                    {
                        var unassigned = await dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstOrDefaultAsync(
                                                                                       x => x.ApplicationName == Constants.UnassignedDumpNames, token);
                        if (unassigned != null)
                        {
                            unassigned.DumpInfos.Remove(entry);
                        }
                    }
                }
                callstack.DumpInfos.Add(entry);
                if (doUpdate)
                    dbContext.Update(callstack);
                else
                    dbContext.Add(callstack);
                await dbContext.SaveChangesAsync(token);
                await dbContext.DisposeAsync();

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
                    _logger.LogInformation(agestoreOutput);
                }
            }
        }

        private async Task CleanupTask(ApplicationDbContext dbContext, CancellationToken token)
        {
            // get all callstacks that do have a FixedVersion set and remove all dump files that are older than 30 days
            if (dbContext.DumpCallstacks == null)
                return;
            var callstacks = await dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).Where(cs => !string.IsNullOrEmpty(cs.FixedVersion)).ToListAsync(token);
            foreach (var callstack in callstacks)
            {
                foreach (var dumpInfo in callstack.DumpInfos)
                {
                    if (dumpInfo.UploadDate.AddDays(_deleteDumpsUploadedBeforeDays) < DateTime.Now)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(dumpInfo.FilePath))
                            {
                                System.IO.File.Delete(Path.Combine(_dumpPath, dumpInfo.FilePath));
                                _logger.LogInformation($"Deleted dump file {Path.Combine(_dumpPath, dumpInfo.FilePath)}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error deleting dump file {Path.Combine(_dumpPath, dumpInfo.FilePath)}");
                        }
                        dumpInfo.FilePath = string.Empty;
                    }
                }
            }
            await dbContext.SaveChangesAsync(token);
        }
    }
}
