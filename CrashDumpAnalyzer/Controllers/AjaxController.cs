using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.WebUtilities;
using System.Net;
using CrashDumpAnalyzer.Utilities;
using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.Models;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using System.IO.Pipelines;
using System;

namespace CrashDumpAnalyzer.Controllers
{

    public class AjaxController : Controller
    {
        private const long MaxFileSize = 10L * 1024L * 1024L * 1024L; // 10GB
        private readonly string[] _permittedExtensions = [".dmp", ".dump"];
        private readonly IConfiguration _configuration;
        private readonly ILogger<AjaxController> _logger;
        private readonly ApplicationDbContext _dbContext;
        private readonly IBackgroundTaskQueue _queue;
        private readonly IServiceProvider _provider;
        private readonly string _dumpPath;
        private readonly string _cdbExe;

        public AjaxController(IConfiguration configuration, ILogger<AjaxController> logger,
                            IServiceProvider provider,
                            ApplicationDbContext dbContext,
                            IBackgroundTaskQueue queue)
        {
            this._configuration = configuration;
            this._logger = logger;
            this._dbContext = dbContext;
            this._queue = queue;
            this._provider = provider;
            Debug.Assert(_dbContext != null);
            Debug.Assert(_queue != null);
            Debug.Assert(_configuration != null);
            Debug.Assert(_logger != null);

            this._dumpPath = _configuration.GetValue<string>("DumpPath") ?? string.Empty;
            this._cdbExe = _configuration.GetValue<string>("CdbExe") ?? "cdb.exe";
        }

        [HttpPost]
        [RequestSizeLimit(MaxFileSize)]
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
                            string trustedFileNameForFileStorage = DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetRandomFileName()+".dmp";
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
                                    ApplicationName = "Unassigned",
                                    ExceptionType = string.Empty,
                                    ApplicationVersion = string.Empty
                                };
                                bool doUpdate = false;
                                if (_dbContext.DumpCallstacks != null)
                                {
                                    var cs = await _dbContext.DumpCallstacks.FirstOrDefaultAsync(
                                        x => x.ApplicationName == "Unassigned");
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

                            _ = _queue.QueueBackgroundWorkItemAsync(async (token) =>
                            {
                                // use cbg to get a callstack from the dump
                                // and then update the database with the callstack
                                string dumpFilePath = Path.Combine(_dumpPath, trustedFileNameForFileStorage);

                                using Process process = new();
                                process.StartInfo.FileName = _cdbExe;
                                process.StartInfo.Arguments = $"-z {dumpFilePath} -c \"!analyze -v; lm lv; q\"";
                                process.StartInfo.RedirectStandardOutput = true;
                                process.Start();
                                StreamReader sr = process.StandardOutput;
                                string output = await sr.ReadToEndAsync(token);
                                await process.WaitForExitAsync(token);

                                // go through the output and find the important bits
                                string context = string.Empty;
                                string callstackString = string.Empty;
                                string processName = string.Empty;
                                string exceptionCode = string.Empty;
                                string version = string.Empty;
                                foreach (var lineString in output.Split(["\n"], StringSplitOptions.TrimEntries))
                                {
                                    if (context == "STACK_TEXT")
                                    {
                                        // the rightmost part is the 'interesting' part for us
                                        var lineParts = lineString.Split([":"], StringSplitOptions.TrimEntries);
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
                                        if (lineString.Contains(processName))
                                        {
                                            context = "MAIN_MODULE";
                                        }
                                    }
                                    if (context=="MAIN_MODULE")
                                    {
                                        if (lineString.Contains("Product version:"))
                                        {
                                            context = string.Empty;
                                            version = lineString.Substring(lineString.IndexOf(':') + 1).Trim();
                                        }
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
                                    if (lineString.Length <= 1 && context.Length > 0)
                                        context = string.Empty;

                                }
                                // we have the call stack, now update the database
                                DumpFileInfo? entry = null;
                                if (dbContext.DumpFileInfos != null)
                                    entry = await dbContext.DumpFileInfos.FirstOrDefaultAsync(x => x.FilePath == dumpFilePath, token);
                                if (entry != null)
                                {
                                    entry.CallStack = callstackString;
                                    entry.ApplicationName = processName;
                                    entry.ExceptionType = exceptionCode;
                                    entry.ApplicationVersion = version;

                                    // find out if we already have this callstack
                                    DumpCallstack callstack = new DumpCallstack
                                    {
                                        Callstack = callstackString,
                                        ApplicationName = processName,
                                        ExceptionType = exceptionCode,
                                        ApplicationVersion = version
                                    };
                                    bool doUpdate = false;
                                    if (dbContext.DumpCallstacks != null)
                                    {
                                        var cs = await dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstOrDefaultAsync(
                                            x => x.Callstack == callstackString, token);
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
                                        var unassigned = await dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstOrDefaultAsync(
                                                                                       x => x.ApplicationName == "Unassigned", token);
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
                                }
                            });

                        }
                    }
                }

                // Drain any remaining section body that hasn't been consumed and
                // read the headers for the next section.
                section = await reader.ReadNextSectionAsync();
            }

            return Created(nameof(AjaxController), null);
        }

        [HttpGet]
        public async Task<IActionResult> DeleteDumpCallstack(int id)
        {
            var dumpCallstack = await _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos).FirstAsync(cs => cs.DumpCallstackId == id);
            if (dumpCallstack == null)
            {
                return NotFound();
            }

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

        [HttpGet]
        public async Task<IActionResult> DownloadFile(int id)
        {
            var entry = await _dbContext.DumpFileInfos.FirstOrDefaultAsync(x => x.DumpFileInfoId == id);
            var fs = new FileStream(entry.FilePath, FileMode.Open);

            // Return the file. A byte array can also be used instead of a stream
            return File(fs, "application/octet-stream", "Dump.dmp");
        }
    }
}
