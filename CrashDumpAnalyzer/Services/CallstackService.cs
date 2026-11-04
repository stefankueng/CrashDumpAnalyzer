using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.Models;
using CrashDumpAnalyzer.Utilities;
using Microsoft.EntityFrameworkCore;

namespace CrashDumpAnalyzer.Services
{
    public class CallstackService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<CallstackService> _logger;
        private readonly DumpCleanupService _cleanup;
        private readonly string _dumpPath;

        public CallstackService(ApplicationDbContext dbContext, IConfiguration configuration, ILogger<CallstackService> logger, DumpCleanupService cleanup)
        {
            _dbContext = dbContext;
            _logger = logger;
            _cleanup = cleanup;
            _dumpPath = configuration.GetValue<string>("DumpPath") ?? string.Empty;
        }

        public async Task<bool> DeleteDumpCallstack(int id, string comment, bool reallyDelete)
        {
            if (_dbContext.DumpCallstacks == null)
                return false;

            var dumpCallstack = await _dbContext.DumpCallstacks
                .Include(cs => cs.DumpInfos)
                .Include(cs => cs.LogFileDatas).ThenInclude(l => l.DumpFileInfo)
                .FirstOrDefaultAsync(cs => cs.DumpCallstackId == id);

            if (dumpCallstack == null)
                return false;
            while (dumpCallstack.LinkedToDumpCallstackId != 0)
            {
                id = dumpCallstack.LinkedToDumpCallstackId;
                dumpCallstack = await _dbContext.DumpCallstacks
                    .Include(cs => cs.DumpInfos)
                    .Include(cs => cs.LogFileDatas).ThenInclude(l => l.DumpFileInfo)
                    .FirstOrDefaultAsync(cs => cs.DumpCallstackId == id);
                if (dumpCallstack == null)
                    return false;
            }

            var linkedIds = await GetLinkedIds(_dbContext, id);
            CallstackComments.Update(dumpCallstack, comment);
            dumpCallstack.Deleted = true;
            await _dbContext.SaveChangesAsync();

            if (dumpCallstack.ApplicationName == Constants.UnassignedDumpNames)
            {
                _dbContext.Remove(dumpCallstack);
                await _dbContext.SaveChangesAsync();
            }
            if (reallyDelete && linkedIds.Count > 0)
            {
                var deletedCount = await _dbContext.DumpCallstacks
                    .Where(cs => linkedIds.Contains(cs.DumpCallstackId)).ExecuteDeleteAsync();
                _logger.LogInformation("Deleted {Count} linked callstacks", deletedCount);
            }

            await _cleanup.CleanupAfterDeletion(CancellationToken.None);
            return true;
        }
        public async Task<bool> UnlinkDumpCallstack(int id)
        {
            if (_dbContext.DumpCallstacks == null)
                return false;
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
                    _logger.LogError(ex, "Error unlinking");
                    throw;
                }
            } while (dumpCallstack != null);

            return true;
        }

        public async Task<bool> DeleteDumpFile(int dumpId)
        {
            if (_dbContext.DumpFileInfos == null)
                return false;
            try
            {
                var dumpToRemove = await _dbContext.DumpFileInfos.FirstOrDefaultAsync(x => x.DumpFileInfoId == dumpId);
                if (dumpToRemove == null)
                    return false;
                try
                {
                    if (!string.IsNullOrEmpty(dumpToRemove.FilePath))
                        System.IO.File.Delete(Path.Combine(_dumpPath, dumpToRemove.FilePath));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deleting dump file {Path}", Path.Combine(_dumpPath, dumpToRemove.FilePath));
                    throw;
                }
                dumpToRemove.FilePath = string.Empty;
                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting callstack from database");
                throw;
            }
            return true;
        }

        public async Task<bool> SetFixedVersion(int id, string? version, string? buildType)
        {
            if (_dbContext.DumpCallstacks == null)
                return false;
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
            }
            return true;
        }

        public async Task<bool> SetApplicationVersion(int id, string? version, string? buildType)
        {
            if (_dbContext.DumpCallstacks == null)
                return false;
            var entry = await _dbContext.DumpCallstacks.FirstOrDefaultAsync(x => x.DumpCallstackId == id);
            if (entry != null)
            {
                entry.ApplicationVersion = version ?? string.Empty;
                entry.BuildType = buildType != null ? BuildTypes.ParseBuildType(buildType) : -1;
                await _dbContext.SaveChangesAsync();
                return true;
            }
            return false;
        }

        public async Task<bool> SetTicket(int id, string? ticket)
        {
            ticket ??= string.Empty;
            if (_dbContext.DumpCallstacks == null)
                return false;
            var entry = await _dbContext.DumpCallstacks.FirstOrDefaultAsync(x => x.DumpCallstackId == id);
            if (entry != null)
            {
                entry.Ticket = ticket.Trim();
                var linkedList = await _dbContext.DumpCallstacks.Where(x => x.LinkedToDumpCallstackId == id).ToListAsync();
                foreach (var linked in linkedList)
                {
                    linked.Ticket = string.Empty;
                }
                await _dbContext.SaveChangesAsync();
            }
            return true;
        }

        public async Task<bool> SetComment(int id, string? comment)
        {
            comment ??= string.Empty;
            _logger.LogInformation("Setting comment for {id} to {comment}", id, comment.Replace(Environment.NewLine, ""));
            if (_dbContext.DumpCallstacks == null)
                return false;
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
            }
            return true;
        }

        public async Task<bool> LinkCallstack(int id, int toId)
        {
            if (_dbContext.DumpCallstacks == null)
                return false;
            if (id == toId)
                return false;
            _logger.LogInformation("Linking callstack {id} to {toId}", id, toId);
            var entry = await _dbContext.DumpCallstacks.FirstOrDefaultAsync(x => x.DumpCallstackId == id);
            var toEntry = await _dbContext.DumpCallstacks.FirstOrDefaultAsync(x => x.DumpCallstackId == toId);
            if (entry != null)
            {
                bool resetDeleted = false;
                if (toEntry != null)
                {
                    if (!toEntry.Deleted)
                        resetDeleted = true;
                    if (!entry.Deleted)
                    {
                        resetDeleted = true;
                        toEntry.Deleted = false;
                    }
                }
                if (resetDeleted)
                    entry.Deleted = false;
                entry.LinkedToDumpCallstackId = toId;
                await _dbContext.SaveChangesAsync();

                do
                {
                    entry = await _dbContext.DumpCallstacks.FirstOrDefaultAsync(x => x.LinkedToDumpCallstackId == id);
                    if (entry != null)
                    {
                        entry.LinkedToDumpCallstackId = toId;
                        if (resetDeleted)
                            entry.Deleted = false;
                        await _dbContext.SaveChangesAsync();
                    }
                } while (entry != null);


            }
            return true;
        }

        private static async Task<List<int>> GetLinkedIds(ApplicationDbContext dbContext, int id)
        {
            if (dbContext.DumpCallstacks == null)
                return [];
            // get all linked callstacks
            List<int> allLinkedCallstacks = [];
            var linkedCallstacks = dbContext.DumpCallstacks.AsNoTracking().Where(x => x.LinkedToDumpCallstackId == id).Select(x => x.DumpCallstackId).ToList();
            if (linkedCallstacks == null || linkedCallstacks.Count == 0)
            {
                return [id];
            }
            // recursively get all linked callstacks
            foreach (var linkedId in linkedCallstacks)
            {
                var subLinked = await GetLinkedIds(dbContext, linkedId);
                if (subLinked != null && subLinked.Count > 0)
                {
                    allLinkedCallstacks.AddRange(subLinked);
                }
            }
            allLinkedCallstacks.AddRange(linkedCallstacks);
            if (!allLinkedCallstacks.Contains(id))
                allLinkedCallstacks.Add(id);
            return allLinkedCallstacks;
        }

        public async Task<int> CountOldEntries(string version)
        {
            return (await FindOldEntries(version)).Count;
        }

        public async Task<List<int>> DeleteOldEntries(string version, int batchSize)
        {
            var entries = await FindOldEntries(version);
            var deletedIds = new List<int>();
            foreach (var entry in entries)
            {
                if (await DeleteDumpCallstack(entry.DumpCallstackId, string.Empty, true))
                    deletedIds.Add(entry.DumpCallstackId);
                if (deletedIds.Count >= batchSize)
                    break;
            }
            _logger.LogInformation("Deleted {Count} old entries without tickets in this batch (older than {Version})", deletedIds.Count, version);
            return deletedIds;
        }

        private async Task<List<DumpCallstack>> FindOldEntries(string version)
        {
            var targetVersion = new SemanticVersion(version, -1);
            var candidates = await _dbContext.Set<DumpCallstack>()
                .Where(cs => string.IsNullOrEmpty(cs.Ticket) && string.IsNullOrEmpty(cs.Comment) && cs.LinkedToDumpCallstackId == 0)
                .ToListAsync();
            var entries = new List<DumpCallstack>();
            foreach (var entry in candidates)
            {
                var entryVersion = new SemanticVersion(entry.ApplicationVersion, entry.BuildType);
                if (entryVersion < targetVersion && entryVersion.Major == targetVersion.Major)
                    entries.Add(entry);
            }
            return entries;
        }
    }
}
