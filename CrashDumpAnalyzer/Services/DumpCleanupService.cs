using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.Models;
using CrashDumpAnalyzer.Utilities;
using Microsoft.EntityFrameworkCore;

namespace CrashDumpAnalyzer.Services
{
    public class DumpCleanupService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<DumpCleanupService> _logger;
        private readonly string _dumpPath;
        private readonly long _deleteDumpsUploadedBeforeDays;

        public DumpCleanupService(ApplicationDbContext dbContext, IConfiguration configuration, ILogger<DumpCleanupService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
            _dumpPath = configuration.GetValue<string>("DumpPath") ?? string.Empty;
            _deleteDumpsUploadedBeforeDays = configuration.GetValue<long>("DeleteDumpsUploadedBeforeDays");
            if (_deleteDumpsUploadedBeforeDays == 0)
                _deleteDumpsUploadedBeforeDays = 30;
        }

        public async Task CleanupTask(CancellationToken token)
        {
            // get all callstacks that do have a FixedVersion set and remove all dump files that are older than 30 days
            if (_dbContext.DumpCallstacks == null)
                return;
            var callstacks = await _dbContext.DumpCallstacks.Include(dumpCallstack => dumpCallstack.DumpInfos)
                .Include(dumpCallstack => dumpCallstack.LogFileDatas)
                            .ThenInclude(logFileLine => logFileLine.DumpFileInfo)
                .Where(cs => !string.IsNullOrEmpty(cs.FixedVersion) || cs.Deleted).ToListAsync(token);

            SortedSet<int> logFilesToDelete = [];
            SortedSet<int> logFilesToKeep = [];
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
                                _logger.LogInformation("Deleted dump file {path}", Path.Combine(_dumpPath, dumpInfo.FilePath));
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error deleting dump file {Path}", Path.Combine(_dumpPath, dumpInfo.FilePath));
                            throw;
                        }
                        dumpInfo.FilePath = string.Empty;
                    }
                }

                foreach (var logLine in callstack.LogFileDatas)
                {
                    if (logLine.DumpFileInfo != null)
                    {
                        if (logLine.LatestTime.AddDays(_deleteDumpsUploadedBeforeDays) < DateTime.Now)
                            logFilesToDelete.Add(logLine.DumpFileInfo.DumpFileInfoId);
                        else
                            logFilesToKeep.Add(logLine.DumpFileInfo.DumpFileInfoId);
                    }
                }
            }
            await _dbContext.SaveChangesAsync(token);

            logFilesToDelete.ExceptWith(logFilesToKeep);
            if (_dbContext.DumpFileInfos != null)
            {
                var filesToDelete = await _dbContext.DumpFileInfos.Where(x => !string.IsNullOrEmpty(x.FilePath) && logFilesToDelete.Contains(x.DumpFileInfoId)).ToListAsync(token);
                foreach (var file in filesToDelete)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(file.FilePath))
                        {
                            System.IO.File.Delete(Path.Combine(_dumpPath, file.FilePath));
                            _logger.LogInformation("Deleted log file {path}", Path.Combine(_dumpPath, file.FilePath));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error deleting log file {path}", Path.Combine(_dumpPath, file.FilePath));
                        throw;
                    }
                    file.FilePath = string.Empty;
                }
            }
            await _dbContext.SaveChangesAsync(token);
            await CleanupOrphanedLogFileData(token);
            await CleanupUnreferencedDumpFiles(token);

        }
        public async Task CleanupAfterDeletion(CancellationToken token)
        {
            await CleanupOrphanedLogFileData(token);
            await CleanupUnreferencedDumpFiles(token);
            await RemoveOrphanedFiles(token);
        }

        private async Task CleanupOrphanedLogFileData(CancellationToken token)
        {
            if (_dbContext.LogFileDatas == null)
                return;

            // Use raw SQL to delete orphaned LogFileData entries directly in the database
            // This is much faster than loading all records into memory
            var deletedCount = await _dbContext.Database.ExecuteSqlRawAsync(@"
                DELETE FROM LogFileData 
                WHERE DumpCallstackId IS NULL OR DumpCallstackId NOT IN (
                    SELECT DumpCallstackId FROM DumpCallstack
                )", token);

            if (deletedCount > 0)
            {
                _logger.LogInformation("Removed {Count} orphaned LogFileData entries from database", deletedCount);
            }
            else
            {
                _logger.LogInformation("No orphaned LogFileData entries found");
            }
        }
        private async Task CleanupUnreferencedDumpFiles(CancellationToken token)
        {
            if (_dbContext.DumpFileInfos == null)
                return;
            if (_dbContext.DumpCallstacks == null)
                return;

            // Get all DumpFileInfos with their relationships
            var allDumpFileInfos = await _dbContext.DumpFileInfos
                .Include(dfi => dfi.DumpCallstack)
                .Where(dfi => !string.IsNullOrWhiteSpace(dfi.FilePath))
                .ToListAsync(cancellationToken: token);

            // Get all DumpFileInfo IDs that are referenced by LogFileDatas
            var referencedByLogFiles = new HashSet<int>();
            if (_dbContext.LogFileDatas != null)
            {
                var logFileReferences = await _dbContext.LogFileDatas
                    .Where(lfd => lfd.DumpFileInfo != null)
                    .Select(lfd => lfd.DumpFileInfo!.DumpFileInfoId)
                    .ToListAsync(cancellationToken: token);

                foreach (var id in logFileReferences)
                {
                    referencedByLogFiles.Add(id);
                }
            }

            var filesToDeleteCompletely = new List<DumpFileInfo>();
            var filesToDeleteOnlyPhysical = new List<DumpFileInfo>();

            foreach (var dumpFileInfo in allDumpFileInfos)
            {
                var dcs = dumpFileInfo.DumpCallstack;
                if (dcs != null && dcs.LinkedToDumpCallstackId != 0)
                {
                    dcs = await _dbContext.DumpCallstacks.FirstOrDefaultAsync(x => x.DumpCallstackId == dcs.LinkedToDumpCallstackId, token);
                }
                bool isReferencedByLogFiles = referencedByLogFiles.Contains(dumpFileInfo.DumpFileInfoId);
                bool isReferencedByActiveCallstack = dcs != null && !dcs.Deleted;
                bool isReferencedByDeletedCallstack = dcs != null && dcs.Deleted;

                // If not referenced by log files and not referenced by any active callstack
                if (!isReferencedByLogFiles && !isReferencedByActiveCallstack)
                {
                    if (isReferencedByDeletedCallstack)
                    {
                        // Only delete the physical file, keep the database entry
                        filesToDeleteOnlyPhysical.Add(dumpFileInfo);
                    }
                    else
                    {
                        // Delete both the physical file and database entry
                        filesToDeleteCompletely.Add(dumpFileInfo);
                    }
                }
            }

            // Delete physical files for both categories
            var allFilesToDeletePhysically = filesToDeleteCompletely.Concat(filesToDeleteOnlyPhysical);
            foreach (var fileInfo in allFilesToDeletePhysically)
            {
                if (!string.IsNullOrEmpty(fileInfo.FilePath))
                {
                    try
                    {
                        var fullPath = Path.Combine(_dumpPath, fileInfo.FilePath);
                        if (System.IO.File.Exists(fullPath))
                        {
                            System.IO.File.Delete(fullPath);
                            _logger.LogInformation("Deleted unreferenced file: {FilePath}", fullPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error deleting file: {FilePath}", Path.Combine(_dumpPath, fileInfo.FilePath));
                        throw;
                    }
                }
                token.ThrowIfCancellationRequested();
            }

            // Clear FilePath for files that should only have physical files deleted
            foreach (var fileInfo in filesToDeleteOnlyPhysical)
            {
                fileInfo.FilePath = string.Empty;
            }

            // Remove database entries for completely unreferenced files
            if (filesToDeleteCompletely.Count > 0)
            {
                _dbContext.DumpFileInfos.RemoveRange(filesToDeleteCompletely);
                _logger.LogInformation("Removed {Count} completely unreferenced DumpFileInfo entries from database",
                    filesToDeleteCompletely.Count);
            }

            if (filesToDeleteOnlyPhysical.Count > 0)
            {
                _logger.LogInformation("Cleared FilePath for {Count} DumpFileInfo entries referenced only by deleted callstacks",
                    filesToDeleteOnlyPhysical.Count);
            }

            try
            {
                await _dbContext.SaveChangesAsync(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving changes to database during cleanup of unreferenced dump files");
                throw;
            }
        }

        private async Task RemoveOrphanedFiles(CancellationToken token)
        {
            // get all dump files in the dump path
            var dumpFilesInDirectory = Directory.GetFiles(_dumpPath);
            if (_dbContext.DumpFileInfos == null)
                return;
            var dumpFilesInDatabase = await _dbContext.DumpFileInfos.ToListAsync(token);
            foreach (var dumpFile in dumpFilesInDirectory)
            {
                var fileName = Path.GetFileName(dumpFile);
                var found = dumpFilesInDatabase.FirstOrDefault(x => x.FilePath == fileName);
                if (found == null)
                {
                    try
                    {
                        System.IO.File.Delete(dumpFile);
                        _logger.LogInformation("Deleted orphaned file {path}", dumpFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error deleting orphaned file {path}", dumpFile);
                        throw;
                    }
                }
            }
        }
    }
}
