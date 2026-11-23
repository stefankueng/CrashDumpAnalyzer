using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.Models;
using CrashDumpAnalyzer.Utilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Cryptography;

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

        public async Task<bool> RegisterUpload(string fileName, long fileSize, string uploadedFromIp, string fileComment, string username, string email, string? checksum = null)
        {
            var uploadDate = DateTime.Now;
            if (checksum != null && await FindDuplicateDumpIdAsync(fileSize, checksum) is int existingId)
            {
                await RecordDuplicateUploadAsync(existingId, uploadDate, uploadedFromIp);
                return false;
            }

            DumpFileInfo entry = new DumpFileInfo
            {
                FilePath = fileName,
                FileSize = fileSize,
                Checksum = checksum,
                UploadDate = uploadDate,
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
                    callstack.FixedVersionSetAt = null;
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
            try
            {
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (checksum != null && IsChecksumConstraintViolation(ex))
            {
                callstack.DumpInfos.Remove(entry);
                _dbContext.Entry(entry).State = EntityState.Detached;
                if (doUpdate)
                    await _dbContext.Entry(callstack).ReloadAsync();
                else
                    _dbContext.Entry(callstack).State = EntityState.Detached;

                if (await FindDumpIdByChecksumAsync(checksum) is not int duplicateOfId)
                    throw;
                await RecordDuplicateUploadAsync(duplicateOfId, uploadDate, uploadedFromIp);
                return false;
            }
        }

        private async Task<int?> FindDuplicateDumpIdAsync(long fileSize, string checksum)
        {
            if (await FindDumpIdByChecksumAsync(checksum) is int id)
                return id;

            var candidates = await _dbContext.Set<DumpFileInfo>()
                .Where(file => file.Checksum == null && file.FileSize == fileSize && file.FilePath != string.Empty)
                .ToListAsync();
            foreach (var candidate in candidates)
            {
                if (!IsDumpFile(candidate.FilePath))
                    continue;

                var existingPath = Path.Combine(_dumpPath, candidate.FilePath);
                if (!File.Exists(existingPath))
                    continue;

                await using var stream = File.OpenRead(existingPath);
                var existingChecksum = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                if (existingChecksum != checksum)
                    continue;

                candidate.Checksum = checksum;
                try
                {
                    await _dbContext.SaveChangesAsync();
                    return candidate.DumpFileInfoId;
                }
                catch (DbUpdateException ex) when (IsChecksumConstraintViolation(ex))
                {
                    await _dbContext.Entry(candidate).ReloadAsync();
                    if (await FindDumpIdByChecksumAsync(checksum) is int duplicateOfId)
                        return duplicateOfId;
                    throw;
                }
            }

            return null;
        }

        private Task<int?> FindDumpIdByChecksumAsync(string checksum) => _dbContext.Set<DumpFileInfo>()
            .Where(file => file.Checksum == checksum)
            .Select(file => (int?)file.DumpFileInfoId)
            .SingleOrDefaultAsync();

        private async Task RecordDuplicateUploadAsync(int dumpFileInfoId, DateTime uploadDate, string uploadedFromIp)
        {
            _dbContext.Add(new DuplicateDumpUpload
            {
                DumpFileInfoId = dumpFileInfoId,
                UploadDate = uploadDate,
                UploadedFromIp = uploadedFromIp
            });
            await _dbContext.SaveChangesAsync();
        }

        private static bool IsDumpFile(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            return extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".dump", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsChecksumConstraintViolation(DbUpdateException exception)
        {
            return exception.InnerException is SqliteException
            {
                SqliteErrorCode: 19,
                SqliteExtendedErrorCode: 2067
            };
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

        public Task<List<DumpCallstack>> FindDumpsToReanalyze(int callstackId)
        {
            return _dbContext.Set<DumpCallstack>().AsNoTracking()
                .Include(cs => cs.DumpInfos)
                .Include(cs => cs.LogFileDatas).ThenInclude(log => log.DumpFileInfo)
                .Where(cs => cs.DumpCallstackId == callstackId || cs.LinkedToDumpCallstackId == callstackId)
                .ToListAsync();
        }

        public Task<bool> HasFilePath(int fileId, string fileName, CancellationToken token)
        {
            return _dbContext.Set<DumpFileInfo>().AnyAsync(file => file.DumpFileInfoId == fileId && file.FilePath == fileName, token);
        }
    }
}
