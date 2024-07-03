using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.WebUtilities;
using System.Net;
using CrashDumpAnalyzer.Utilities;
using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.Models;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.RegularExpressions;

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
        private long _maxCacheSize;

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
                    else
                    {
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
                                    FilePath = Path.Combine(_dumpPath, trustedFileNameForFileStorage),
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
                                catch (Exception)
                                {
                                }
                                var dumpData = await dumpAnalyzeTask;

                                await UpdateDumpDataInDatabase(dbContext, dumpFilePath, uploadedFromHostname, dumpData, null, token);
                            });

                        }
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
                            System.IO.File.Delete(dumpInfo.FilePath);
                    }
                    catch (Exception)
                    {
                    }
                }
                dumpCallstack.DumpInfos.Clear();
                await _dbContext.SaveChangesAsync();
                var dumpCallstacks = _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).Where(cs => cs.LinkedToDumpCallstackId == id);
                foreach (var callStack in dumpCallstacks)
                {
                    callStack.Deleted = true;
                    foreach (var dumpInfo in callStack.DumpInfos)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(dumpInfo.FilePath))
                                System.IO.File.Delete(dumpInfo.FilePath);
                        }
                        catch (Exception)
                        {
                        }
                    }
                    callStack.DumpInfos.Clear();
                    await _dbContext.SaveChangesAsync();
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
            var dumpCallstack = await _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos)
                .FirstAsync(cs => (cs.DumpCallstackId == callstackId || cs.LinkedToDumpCallstackId == callstackId) && cs.DumpInfos.First(x => x.DumpFileInfoId == dumpId) != null);

            try
            {
                var dumpToRemove = dumpCallstack.DumpInfos.First(x => x.DumpFileInfoId == dumpId);
                if (dumpToRemove != null)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(dumpToRemove.FilePath))
                            System.IO.File.Delete(dumpToRemove.FilePath);
                    }
                    catch (Exception)
                    {
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
            var fs = new FileStream(entry.FilePath, FileMode.Open);

            // Return the file. A byte array can also be used instead of a stream
            return File(fs, "application/octet-stream", "Dump.dmp");
        }

        [HttpPost]
        public async Task<IActionResult> SetFixedVersion(int id, string? version)
        {
            if (_dbContext.DumpCallstacks == null)
                return NotFound();
            var entry = await _dbContext.DumpCallstacks.FirstOrDefaultAsync(x => x.DumpCallstackId == id);
            if (entry != null)
            {
                entry.FixedVersion = version ?? string.Empty;
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
                entry.Ticket = ticket ?? string.Empty;
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
                entry.Comment = comment ?? string.Empty;
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
            var dumpCallstack = await _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos)
                .FirstAsync(cs => (cs.DumpCallstackId == callstackId || cs.LinkedToDumpCallstackId == callstackId));
            if (dumpCallstack == null)
                return NotFound();
            try
            {
                var dumpToAnalyze = dumpCallstack.DumpInfos.First(x => !string.IsNullOrEmpty(x.FilePath));
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
                            var dumpData = await dumpAnalyzer.AnalyzeDump(dumpToAnalyze.FilePath, token);
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

                // find out if we already have this callstack
                DumpCallstack callstack = new DumpCallstack
                {
                    Callstack = dumpData.callstackString,
                    CleanCallstack = dumpData.cleanCallstackString,
                    ApplicationName = dumpData.processName,
                    ExceptionType = dumpData.exceptionCode,
                    ApplicationVersion = dumpData.version
                };
                bool doUpdate = false;
                if (dbContext.DumpCallstacks != null)
                {
                    try
                    {
                        var cs = await dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstOrDefaultAsync(
                            x => callstackId != null ? (x.DumpCallstackId == callstackId) : (x.CleanCallstack == dumpData.cleanCallstackString && x.ApplicationName == dumpData.processName), token);

                        if (cs != null)
                        {
                            callstack = cs;
                            callstack.Deleted = false;
                            var v1 = new SemanticVersion(dumpData.version);
                            var v2 = new SemanticVersion(callstack.ApplicationVersion);
                            if (v1 >= v2)
                            {
                                callstack.ApplicationVersion = dumpData.version;
                            }
                            if (callstackId != null)
                            {
                                cs.CleanCallstack = dumpData.cleanCallstackString;
                                cs.Callstack = dumpData.callstackString;
                            }
                            doUpdate = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error parsing callstack");
                    }
                    if (callstackId != null)
                    {

                    }
                    else
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
    }
}
