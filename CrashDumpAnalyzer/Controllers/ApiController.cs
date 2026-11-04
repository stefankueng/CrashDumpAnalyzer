using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.WebUtilities;
using System.Net;
using System.Security.Claims;
using CrashDumpAnalyzer.Services;
using CrashDumpAnalyzer.Utilities;

namespace CrashDumpAnalyzer.Controllers
{
    [ApiController]
    [Route("Api/[Action]")]
    public class ApiController : Controller
    {
        private const long MaxFileSize = long.MaxValue;
        private readonly ILogger<ApiController> _logger;
        private readonly DumpFileService _files;
        private readonly CallstackService _callstacks;
        private readonly DumpAnalysisQueue _analysis;
        private readonly string _dumpPath;
        private readonly string[] _permittedExtensions;

        public ApiController(IConfiguration configuration, ILogger<ApiController> logger,
            DumpFileService files, CallstackService callstacks, DumpAnalysisQueue analysis)
        {
            _logger = logger;
            _files = files;
            _callstacks = callstacks;
            _analysis = analysis;
            _dumpPath = configuration.GetValue<string>("DumpPath") ?? string.Empty;
            var logExtensions = configuration.GetSection("LogFileAnalyzer").GetValue<string>("FileExtensions", ".txt;.log").Split(';');
            _permittedExtensions = [".dmp", ".dump", .. logExtensions];
        }

        [EndpointSummary("Dump and log file upload")]
        [EndpointDescription("Upload the dump and log files to analyze")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
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

            string fileComment = string.Empty;
            if (section != null)
            {
                bool hasContentDispositionHeader =
                    ContentDispositionHeaderValue.TryParse(
                        section.ContentDisposition, out ContentDispositionHeaderValue? contentDisposition);
                if (hasContentDispositionHeader)
                {
                    if (contentDisposition != null && !MultipartRequestHelper
                            .HasFileContentDisposition(contentDisposition))
                    {
                        if (contentDisposition.Name == "fileComment")
                        {
                            using (var stringReader = new StreamReader(section.Body))
                            {
                                fileComment = await stringReader.ReadToEndAsync();
                            }
                            section = await reader.ReadNextSectionAsync();
                        }
                    }
                }
            }

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
                            "The request couldn't be processed. If you provide a comment field, make sure it's first in the header order and the file byte arrays come after. (Error 2).");
                        // Log error

                        return BadRequest(ModelState);
                    }
                    // Don't trust the file name sent by the client. To display
                    // the file name, HTML-encode the value.

                    if (contentDisposition != null)
                    {
                        string? fileNameForDisplay = WebUtility.HtmlEncode(
                            contentDisposition.FileName.Value);
                        var ext = Path.GetExtension(contentDisposition.FileName.Value);
                        string fileNameForFileStorage = DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetRandomFileName() + ext;
                        long fileSize;
                        await using (FileStream targetStream = System.IO.File.Create(
                                         Path.Combine(_dumpPath, fileNameForFileStorage), 10_000_000))
                        {
                            var success = await FileHelpers.ProcessStreamedFile(
                                section, contentDisposition, ModelState,
                                _permittedExtensions, MaxFileSize, targetStream);

                            if (!ModelState.IsValid || !success)
                            {
                                targetStream.Close();
                                System.IO.File.Delete(Path.Combine(_dumpPath, fileNameForFileStorage));
                                return BadRequest(ModelState);
                            }
                            fileSize = targetStream.Length;

                            _logger.LogInformation(
                                "Uploaded file '{FileNameForDisplay}' saved to " +
                                "'{TargetFilePath}' as {FileNameForFileStorage}",
                                fileNameForDisplay, _dumpPath,
                                fileNameForFileStorage);
                        }
                        await _files.RegisterUpload(fileNameForFileStorage, fileSize, uploadedFromIp,
                            fileComment, User.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
                            User.FindFirstValue(ClaimTypes.Email) ?? string.Empty);
                        await _analysis.QueueUpload(fileNameForFileStorage, uploadedFromIp);
                    }
                }
                // Drain any remaining section body that hasn't been consumed and
                // read the headers for the next section.
                section = await reader.ReadNextSectionAsync();
            }

            return Created(nameof(ApiController), null);
        }

        [EndpointSummary("Delete the callstack with the specified id")]
        [HttpPost]
        public async Task<IActionResult> DeleteDumpCallstack(int id, bool reallyDelete)
        {
            return await _callstacks.DeleteDumpCallstack(id, string.Empty, reallyDelete) ? NoContent() : NotFound();
        }

        [EndpointSummary("Count entries with version lower than specified that don't have tickets assigned")]
        [HttpGet]
        public async Task<IActionResult> CountOldEntries(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return BadRequest("Version is required");
            try
            {
                var count = await _callstacks.CountOldEntries(version);
                return Ok(new { count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error counting old entries");
                return StatusCode(500, "Error counting old entries");
            }
        }

        [EndpointSummary("Delete entries with version lower than specified that don't have tickets assigned")]
        [HttpPost]
        public async Task<IActionResult> DeleteOldEntries(string version, int batchSize = 10)
        {
            if (string.IsNullOrWhiteSpace(version))
                return BadRequest("Version is required");
            try
            {
                var deletedIds = await _callstacks.DeleteOldEntries(version, batchSize);
                return Ok(new { deletedCount = deletedIds.Count, deletedIds });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting old entries");
                return StatusCode(500, "Error deleting old entries");
            }
        }

        [EndpointSummary("Unlink the callstack with the specified id")]
        [HttpPost]
        public async Task<IActionResult> UnlinkDumpCallstack(int id)
        {
            return await _callstacks.UnlinkDumpCallstack(id) ? NoContent() : NotFound();
        }

        [EndpointSummary("Deletes the dump or log file for the specified dumpId")]
        [HttpPost]
        public async Task<IActionResult> DeleteDumpFile(int dumpId)
        {
            return await _callstacks.DeleteDumpFile(dumpId) ? NoContent() : NotFound();
        }

        [EndpointSummary("Download the dump/log file with the specified id")]
        [HttpGet("{id}")]
        public async Task<IActionResult> DownloadFile(int id)
        {
            var download = await _files.OpenDownload(id);
            if (download == null)
                return NotFound();
            return File(download.Content, download.ContentType, download.FileName);
        }

        [EndpointSummary("Set the 'version' number where the problem of the callstack with 'id' is fixed")]
        [HttpPost]
        public async Task<IActionResult> SetFixedVersion(int id, string? version, string? buildType)
        {
            await _callstacks.SetFixedVersion(id, version, buildType);
            return NoContent();
        }

        [EndpointSummary("Set the 'version' and build type for the application with 'id'")]
        [HttpPost]
        public async Task<IActionResult> SetApplicationVersion(int id, string? version, string? buildType)
        {
            return await _callstacks.SetApplicationVersion(id, version, buildType) ? NoContent() : BadRequest();
        }

        [EndpointSummary("Set the ticket assigned to the callstack with id")]
        [HttpPost]
        public async Task<IActionResult> SetTicket(int id, string? ticket)
        {
            await _callstacks.SetTicket(id, ticket);
            return NoContent();
        }

        [EndpointSummary("Set the comment for the callstack with id")]
        [HttpPost]
        public async Task<IActionResult> SetComment(int id, string? comment)
        {
            await _callstacks.SetComment(id, comment);
            return NoContent();
        }

        [EndpointSummary("Link the callstack with 'id' to the callstack with 'toId'")]
        [HttpPost]
        public async Task<IActionResult> LinkCallstack(int id, int toId)
        {
            return await _callstacks.LinkCallstack(id, toId) ? NoContent() : BadRequest();
        }

        [EndpointSummary("Analyze the first dump file of callstack 'callstackId' again'")]
        [HttpPost]
        public async Task<IActionResult> ReAnalyzeDumpFile(int callstackId)
        {
            var dump = await _files.FindDumpToReanalyze(callstackId);
            if (dump == null)
                return NotFound();
            if (dump.DumpInfos.FirstOrDefault(x => !string.IsNullOrEmpty(x.FilePath)) is { } file)
                await _analysis.QueueReanalysis(file.FilePath, dump.ApplicationName == Constants.UnassignedDumpNames ? null : callstackId);
            return NoContent();
        }
    }
}
