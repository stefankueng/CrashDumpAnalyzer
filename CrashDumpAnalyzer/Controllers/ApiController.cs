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
        private readonly Regex _cleanCallstackRegex;
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

            string pattern = @"^((.*)\+0x([a-f0-9]+)|0x.*)$";
            RegexOptions options = RegexOptions.Multiline;
            _cleanCallstackRegex = new Regex(pattern, options);
        }

        private string RemoveEmptyLines(string lines)
        {
            return Regex.Replace(lines, @"^\s*$\n|\r", string.Empty, RegexOptions.Multiline).TrimEnd();
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
                            byte[] streamedFileContent = await FileHelpers.ProcessStreamedFile(
                                section, contentDisposition, ModelState,
                                _permittedExtensions, MaxFileSize);

                            if (!ModelState.IsValid)
                            {
                                return BadRequest(ModelState);
                            }

                            await using (FileStream targetStream = System.IO.File.Create(
                                       Path.Combine(_dumpPath, trustedFileNameForFileStorage)))
                            {
                                await targetStream.WriteAsync(streamedFileContent);
                                DumpFileInfo entry = new DumpFileInfo
                                {
                                    FilePath = Path.Combine(_dumpPath, trustedFileNameForFileStorage),
                                    FileSize = targetStream.Length,
                                    UploadDate = DateTime.Now
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

                                using Process process = new();
                                process.StartInfo.FileName = _cdbExe;
                                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(_cdbExe);
                                process.StartInfo.Arguments = $"-netsyms:yes -lines -z {dumpFilePath} -c \"!analyze -v; lm lv; q\"";
                                process.StartInfo.EnvironmentVariables["_NT_SYMBOL_PATH"] = _symbolPath;
                                process.StartInfo.EnvironmentVariables["_NT_SOURCE_PATH "] = "srv\\*";
                                process.StartInfo.RedirectStandardOutput = true;
                                process.Start();
                                StreamReader sr = process.StandardOutput;
                                string output = await sr.ReadToEndAsync(token);
                                await process.WaitForExitAsync(token);

                                // go through the output and find the important bits
                                _logger.LogInformation(output);
                                string context = string.Empty;
                                string callstackString = string.Empty;
                                string processName = string.Empty;
                                string exceptionCode = string.Empty;
                                string version = string.Empty;
                                DateTime dumpTime = DateTime.Now;
                                foreach (var lineString in output.Split(["\n"], StringSplitOptions.TrimEntries))
                                {
                                    if (context == "STACK_TEXT")
                                    {
                                        // the rightmost part is the 'interesting' part for us
                                        var lineParts = lineString.Split([" : "], StringSplitOptions.TrimEntries);
                                        if (lineParts.Length == 3)
                                        {
                                            callstackString += lineParts[2] + "\n";
                                        }
                                        if (lineParts.Length == 2)
                                        {
                                            callstackString += lineParts[1] + "\n";
                                        }
                                        if (lineParts.Length == 1)
                                        {
                                            callstackString += lineParts[0].Substring(lineParts[0].LastIndexOf(' ') + 1) + "\n";
                                        }
                                    }
                                    if (context == "VERSION")
                                    {
                                        context = string.Empty;
                                        version = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                                    }
                                    if (context == "MODULES")
                                    {
                                        if (lineString.Contains(processName) || (lineString.Length > 0 && processName == "unknown"))
                                        {
                                            context = "MAIN_MODULE";
                                        }
                                    }
                                    if (context == "MAIN_MODULE")
                                    {
                                        if (lineString.Contains("Image name:") && processName == "unknown" && lineString.Contains(".exe"))
                                        {
                                            processName = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                                        }
                                        if (lineString.Contains("Product version:") && processName != "unknown")
                                        {
                                            context = string.Empty;
                                            version = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                                        }
                                    }
                                    if (lineString.Contains("Could not open dump file"))
                                    {
                                        callstackString = lineString;
                                        context = "STACK_TEXT";
                                    }
                                    if (lineString.Contains("STACK_TEXT:"))
                                    {
                                        context = "STACK_TEXT";
                                    }
                                    if (lineString.Contains("---------"))
                                    {
                                        context = "MODULES";
                                    }
                                    if (lineString.Contains("Key  : WER.Process.Version"))
                                    {
                                        context = "VERSION";
                                    }
                                    if (lineString.Contains("PROCESS_NAME:"))
                                    {
                                        context = string.Empty;
                                        processName = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                                    }
                                    if (lineString.Contains("ExceptionCode:"))
                                    {
                                        context = string.Empty;
                                        exceptionCode = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                                    }
                                    if (lineString.Contains("Debug session time:"))
                                    {
                                        context = string.Empty;
                                        var dateString = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                                        dateString = dateString.Replace("UTC + ", "UTC +");
                                        dateString = dateString.Replace("UTC - ", "UTC -");
                                        dateString = dateString.Replace("  ", " ");
                                        try
                                        {
                                            var dt = DateTime.ParseExact(dateString, "ddd MMM d HH:mm:ss.fff yyyy (UTC z:00)", new CultureInfo("en-us"));
                                            dumpTime = dt;
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogError(ex, "Error parsing dump time");
                                        }
                                    }
                                    if (lineString.Length <= 1 && context.Length > 0 && context != "MODULES")
                                        context = string.Empty;

                                }
                                // we have the call stack, now update the database
                                DumpFileInfo? entry = null;
                                if (dbContext.DumpFileInfos != null)
                                    entry = await dbContext.DumpFileInfos.FirstOrDefaultAsync(x => x.FilePath == dumpFilePath, token);

                                // filter out offsets and lines with no symbols
                                string cleanCallstackString = _cleanCallstackRegex.Replace(callstackString, @"$2");
                                cleanCallstackString = RemoveEmptyLines(cleanCallstackString);
                                if (entry != null)
                                {
                                    entry.CallStack = callstackString;
                                    entry.ApplicationName = processName;
                                    entry.ExceptionType = exceptionCode;
                                    entry.ApplicationVersion = version;
                                    entry.DumpTime = dumpTime;

                                    // find out if we already have this callstack
                                    DumpCallstack callstack = new DumpCallstack
                                    {
                                        Callstack = callstackString,
                                        CleanCallstack = cleanCallstackString,
                                        ApplicationName = processName,
                                        ExceptionType = exceptionCode,
                                        ApplicationVersion = version
                                    };
                                    bool doUpdate = false;
                                    if (dbContext.DumpCallstacks != null)
                                    {

                                        try
                                        {
                                            var cs = await dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstOrDefaultAsync(
                                                x => x.CleanCallstack == cleanCallstackString, token);

                                            if (cs != null)
                                            {
                                                callstack = cs;
                                                var v1 = new SemanticVersion(version);
                                                var v2 = new SemanticVersion(callstack.ApplicationVersion);
                                                if (v1 >= v2)
                                                {
                                                    callstack.ApplicationVersion = version;
                                                }
                                                doUpdate = true;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogError(ex, "Error parsing callstack");
                                        }
                                        var unassigned = await dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstOrDefaultAsync(
                                                                                       x => x.ApplicationName == Constants.UnassignedDumpNames, token);
                                        if (unassigned != null)
                                        {
                                            unassigned.DumpInfos.Remove(entry);
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
                                    if (_agestoreExe!=string.Empty)
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
                _dbContext.DumpCallstacks.Remove(dumpCallstack);
                await _dbContext.SaveChangesAsync();
                // now delete all dump files from this callstack
                foreach (var dumpInfo in dumpCallstack.DumpInfos)
                {
                    System.IO.File.Delete(dumpInfo.FilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting callstack from database");
            }
            return NoContent();
        }
        [HttpPost]
        public async Task<IActionResult> DeleteDumpFile(int callstackId, int dumpId)
        {
            if (_dbContext.DumpCallstacks == null)
                return NotFound();
            var dumpCallstack = await _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstAsync(cs => cs.DumpCallstackId == callstackId);
            if (dumpCallstack.DumpCallstackId != callstackId)
                return NotFound();

            try
            {
                var dumpToRemove = dumpCallstack.DumpInfos.First(x => x.DumpFileInfoId == dumpId);
                if (dumpToRemove != null)
                {
                    System.IO.File.Delete(dumpToRemove.FilePath);
                    dumpCallstack.DumpInfos.Remove(dumpToRemove);
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
        public async Task<IActionResult> SetFixedVersion(int id, string version)
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

    }
}
