using CrashDumpAnalyzer.Utilities;

namespace CrashDumpAnalyzer.Services
{
	public class QueuedHostedService : BackgroundService
	{
		private readonly ILogger _logger;
		private readonly int _executorsCount = 1;
		public IBackgroundTaskQueue TaskQueue { get; }

		public QueuedHostedService(IBackgroundTaskQueue taskQueue,
			ILoggerFactory loggerFactory,
			IConfiguration configuration)
		{
			TaskQueue = taskQueue;
			_logger = loggerFactory.CreateLogger<QueuedHostedService>();
			if (ushort.TryParse(configuration["MaxNumOfParallelOperations"], out var count))
			{
				if (count == 0)
					throw new ArgumentOutOfRangeException(nameof(configuration), "MaxNumOfParallelOperations must be greater than zero.");
				_executorsCount = count;
			}
		}

		protected override Task ExecuteAsync(CancellationToken stoppingToken)
		{
			_logger.LogInformation("Queued Hosted Service is starting.");
			return Task.WhenAll(Enumerable.Range(0, _executorsCount).Select(_ => ExecuteWorker(stoppingToken)));
		}

		private async Task ExecuteWorker(CancellationToken stoppingToken)
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					_logger.LogInformation("Waiting background task...");
					var workItem = await TaskQueue.DequeueAsync(stoppingToken);
					_logger.LogInformation("Got background task, executing...");
					await workItem(stoppingToken);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					break;
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error occurred executing background work item.");
				}
			}
		}

		public override async Task StopAsync(CancellationToken cancellationToken)
		{
			_logger.LogInformation("Queued Hosted Service is stopping.");
			await base.StopAsync(cancellationToken);
		}
	}
}
