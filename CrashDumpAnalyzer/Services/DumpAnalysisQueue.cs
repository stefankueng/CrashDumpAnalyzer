using CrashDumpAnalyzer.Utilities;
using System.Net;
using System.Net.Sockets;

namespace CrashDumpAnalyzer.Services
{
    public class DumpAnalysisQueue
    {
        private readonly IBackgroundTaskQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DumpAnalysisQueue> _logger;
        private readonly string _dumpPath;
        private readonly string _cdbExe;
        private readonly string _symbolPath;
        private readonly string[] _logfileFileExts;

        public DumpAnalysisQueue(IBackgroundTaskQueue queue, IServiceScopeFactory scopeFactory,
            IConfiguration configuration, ILogger<DumpAnalysisQueue> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
            _dumpPath = configuration.GetValue<string>("DumpPath") ?? string.Empty;
            _cdbExe = configuration.GetValue<string>("CdbExe") ?? "cdb.exe";
            _symbolPath = configuration.GetValue<string>("SymbolPath") ?? string.Empty;
            _logfileFileExts = configuration.GetSection("LogFileAnalyzer").GetValue<string>("FileExtensions", ".txt;.log").Split(';');
        }

        public ValueTask QueueUpload(string fileName, string uploadedFromIp)
        {
            return _queue.QueueBackgroundWorkItemAsync(async token =>
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var results = scope.ServiceProvider.GetRequiredService<AnalysisResultService>();
                var cleanup = scope.ServiceProvider.GetRequiredService<DumpCleanupService>();
                var filePath = Path.Combine(_dumpPath, fileName);
                Task<DumpData>? dumpTask = null;
                Task<Dictionary<LogIssue, List<(DateTime date, long lineNumber)>>>? logTask = null;
                if (_logfileFileExts.Contains(Path.GetExtension(fileName), StringComparer.InvariantCultureIgnoreCase))
                    logTask = new LogAnalyzer(_logger, _configuration).Analyze(filePath, token);
                else
                    dumpTask = new DumpAnalyzer(_cdbExe, _symbolPath, _logger).AnalyzeDump(filePath, token);

                var hostname = "unknown host";
                try
                {
                    var host = await Dns.GetHostEntryAsync(IPAddress.Parse(uploadedFromIp));
                    hostname = host.HostName.Split('.')[0];
                }
                catch (Exception ex) when (ex is SocketException or ArgumentException)
                {
                    _logger.LogWarning(ex, "Failed to resolve hostname for IP {IpAddress}", uploadedFromIp);
                }

                if (dumpTask != null)
                {
                    var data = await dumpTask;
                    if (string.IsNullOrWhiteSpace(data.cleanCallstackString) && string.IsNullOrWhiteSpace(data.processName))
                    {
                        data.processName = Constants.InvalidFilesNames;
                        data.cleanCallstackString = Constants.InvalidFilesNames;
                    }
                    await cleanup.CleanupTask(token);
                    await results.UpdateDumpDataInDatabase(fileName, hostname, data, null, token);
                }
                else if (logTask != null)
                {
                    var data = await logTask;
                    await cleanup.CleanupTask(token);
                    await results.UpdateLogDataInDatabase(fileName, hostname, data, token);
                }
            });
        }

        public ValueTask QueueReanalysis(string fileName, int? callstackId)
        {
            return _queue.QueueBackgroundWorkItemAsync(async token =>
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var results = scope.ServiceProvider.GetRequiredService<AnalysisResultService>();
                var analyzer = new DumpAnalyzer(_cdbExe, _symbolPath, _logger);
                var data = await analyzer.AnalyzeDump(Path.Combine(_dumpPath, fileName), token);
                await results.UpdateDumpDataInDatabase(fileName, null, data, callstackId, token);
            });
        }
    }
}
