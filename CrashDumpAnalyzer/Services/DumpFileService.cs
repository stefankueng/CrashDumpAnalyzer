using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.Models;
using CrashDumpAnalyzer.Utilities;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace CrashDumpAnalyzer.Services
{
    public class DumpFileService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly string _dumpPath;
        private readonly string[] _logfileFileExts;

        public DumpFileService(ApplicationDbContext dbContext, IConfiguration configuration)
        {
            _dbContext = dbContext;
            _dumpPath = configuration.GetValue<string>("DumpPath") ?? string.Empty;
            _logfileFileExts = configuration.GetSection("LogFileAnalyzer").GetValue<string>("FileExtensions", ".txt;.log").Split(';');
        }

        public async Task RegisterUpload(string fileName, long fileSize, string uploadedFromIp, string fileComment, string username, string email)
        {
            DumpFileInfo entry = new DumpFileInfo
            {
                FilePath = fileName,
                FileSize = fileSize,
                UploadDate = DateTime.Now,
                UploadedFromIp = uploadedFromIp,
                Comment = fileComment,
                UploadedFromUsername = username,
                UploadedFromUserEmail = email
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
            await _dbContext.SaveChangesAsync();
        }

        public async Task<DumpDownload?> OpenDownload(int id)
        {
            if (_dbContext.DumpFileInfos == null)
                return null;
            var entry = await _dbContext.DumpFileInfos.FirstOrDefaultAsync(x => x.DumpFileInfoId == id);
            if (entry == null)
                return null;
            var filePath = Path.Combine(_dumpPath, entry.FilePath);
            if (!_logfileFileExts.Contains(Path.GetExtension(entry.FilePath), StringComparer.InvariantCultureIgnoreCase))
                return new DumpDownload(File.OpenRead(filePath), "application/octet-stream", "Dump.dmp");

            using var reader = File.OpenText(filePath);
            var memoryStream = new MemoryStream();
            try
            {
                await using (var writer = new StreamWriter(memoryStream, leaveOpen: true))
                {
                    await writer.WriteLineAsync("<!DOCTYPE html><html><head><style>:target { background-color: #ff0 }</style></head><body style=\"font-family: monospace; text-wrap: nowrap; white-space: nowrap;\">");
                    long lineNumber = 0;
                    while (await reader.ReadLineAsync() is { } line)
                    {
                        ++lineNumber;
                        await writer.WriteLineAsync($"<div id=\"{lineNumber}\">{WebUtility.HtmlEncode(line)}</div>");
                    }
                    await writer.WriteLineAsync("</body></html>");
                }
                memoryStream.Position = 0;
                return new DumpDownload(memoryStream, "text/html", string.Empty);
            }
            catch
            {
                await memoryStream.DisposeAsync();
                throw;
            }
        }

        public Task<DumpCallstack?> FindDumpToReanalyze(int callstackId)
        {
            return _dbContext.Set<DumpCallstack>().Include(cs => cs.DumpInfos)
                .FirstOrDefaultAsync(cs => cs.DumpCallstackId == callstackId || cs.LinkedToDumpCallstackId == callstackId);
        }
    }
}
