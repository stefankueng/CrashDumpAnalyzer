using CrashDumpAnalyzer.Utilities;

namespace CrashDumpAnalyzer.Services
{

	public class QueuedHostedService : IHostedService
	{
		private readonly ILogger _logger;

		private readonly Task[] _executors;
		private readonly int _executorsCount = 2; //--default value: 2
		private CancellationTokenSource? _tokenSource;
		public IBackgroundTaskQueue TaskQueue { get; }

		public QueuedHostedService(IBackgroundTaskQueue taskQueue,
			ILoggerFactory loggerFactory,
			IConfiguration configuration)
		{
			TaskQueue = taskQueue;
			_logger = loggerFactory.CreateLogger<QueuedHostedService>();

			if (ushort.TryParse(configuration["App:MaxNumOfParallelOperations"], out var ct))
			{
				_executorsCount = ct;
			}
			_executors = new Task[_executorsCount];
		}

		public Task StartAsync(CancellationToken cancellationToken)
		{
			_logger.LogInformation("Queued Hosted Service is starting.");

			_tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

			for (var i = 0; i < _executorsCount; i++)
			{
				var executorTask = new Task(
					async () =>
					{
						while (!cancellationToken.IsCancellationRequested)
						{
							_logger.LogInformation("Waiting background task...");
							var workItem = await TaskQueue.DequeueAsync(cancellationToken);

							try
							{
								_logger.LogInformation("Got background task, executing...");
								await workItem(cancellationToken);
							}
							catch (Exception ex)
							{
								_logger.LogError(ex,
									"Error occurred executing {WorkItem}.", nameof(workItem)
								);
							}
						}
					}, _tokenSource.Token);

				_executors[i] = executorTask;
				executorTask.Start();
			}

			return Task.CompletedTask;
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			_logger.LogInformation("Queued Hosted Service is stopping.");
			_tokenSource?.Cancel(); // send the cancellation signal

			// wait for _executors completion
			Task.WaitAll(_executors, cancellationToken);

			return Task.CompletedTask;
		}
	}
}
