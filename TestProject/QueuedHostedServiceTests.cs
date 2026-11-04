using CrashDumpAnalyzer.Services;
using CrashDumpAnalyzer.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace CrashDumpAnalyzer.Tests
{
    [TestClass]
    public class QueuedHostedServiceTests
    {
        [TestMethod]
        public async Task Stop_CancelsActiveWorkAndAwaitsItsCompletion()
        {
            var queue = new BackgroundTaskQueue();
            using var loggerFactory = LoggerFactory.Create(_ => { });
            using var worker = new QueuedHostedService(queue, loggerFactory, new ConfigurationBuilder().Build());
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await queue.QueueBackgroundWorkItemAsync(async token =>
            {
                using var registration = token.Register(() => cancelled.TrySetResult());
                started.SetResult();
                await finish.Task.WaitAsync(timeout.Token);
            });
            await worker.StartAsync(timeout.Token);
            try
            {
                await started.Task.WaitAsync(timeout.Token);

                var stopping = worker.StopAsync(timeout.Token);
                await cancelled.Task.WaitAsync(timeout.Token);
                Assert.IsFalse(stopping.IsCompleted);
                finish.SetResult();
                await stopping;
                Assert.IsNotNull(worker.ExecuteTask);
                Assert.IsTrue(worker.ExecuteTask.IsCompletedSuccessfully);
            }
            finally
            {
                finish.TrySetResult();
                await worker.StopAsync(timeout.Token);
            }
        }

        [TestMethod]
        public async Task Stop_CancelsIdleDequeueWithoutErrors()
        {
            var logger = new Mock<ILogger<QueuedHostedService>>();
            var factory = new Mock<ILoggerFactory>();
            factory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(logger.Object);
            using var worker = new QueuedHostedService(new BackgroundTaskQueue(), factory.Object, new ConfigurationBuilder().Build());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await worker.StartAsync(timeout.Token);

            await worker.StopAsync(timeout.Token);

            Assert.IsNotNull(worker.ExecuteTask);
            Assert.IsTrue(worker.ExecuteTask.IsCompletedSuccessfully);
            VerifyErrorCount(logger, Times.Never());
        }

        [TestMethod]
        public async Task FailedJob_IsLoggedAndDoesNotStopTheNextJob()
        {
            var queue = new BackgroundTaskQueue();
            var logger = new Mock<ILogger<QueuedHostedService>>();
            var factory = new Mock<ILoggerFactory>();
            factory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(logger.Object);
            using var worker = new QueuedHostedService(queue, factory.Object, new ConfigurationBuilder().Build());
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await queue.QueueBackgroundWorkItemAsync(_ => ValueTask.FromException(new InvalidOperationException("test failure")));
            await queue.QueueBackgroundWorkItemAsync(_ =>
            {
                completed.SetResult();
                return ValueTask.CompletedTask;
            });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await worker.StartAsync(timeout.Token);
            try
            {
                await completed.Task.WaitAsync(timeout.Token);
                VerifyErrorCount(logger, Times.Once());
            }
            finally
            {
                await worker.StopAsync(timeout.Token);
            }
        }

        [TestMethod]
        public async Task ConfiguredParallelism_RunsMultipleJobsConcurrently()
        {
            var queue = new BackgroundTaskQueue();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MaxNumOfParallelOperations"] = "2"
            }).Build();
            using var loggerFactory = LoggerFactory.Create(_ => { });
            using var worker = new QueuedHostedService(queue, loggerFactory, configuration);
            var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await queue.QueueBackgroundWorkItemAsync(async token =>
            {
                first.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            });
            await queue.QueueBackgroundWorkItemAsync(async token =>
            {
                second.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            });
            await worker.StartAsync(timeout.Token);
            try
            {
                await Task.WhenAll(first.Task, second.Task).WaitAsync(timeout.Token);
            }
            finally
            {
                await worker.StopAsync(timeout.Token);
            }
        }

        [TestMethod]
        public void ZeroWorkers_IsRejectedInsteadOfLeavingJobsUnprocessed()
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MaxNumOfParallelOperations"] = "0"
            }).Build();
            using var loggerFactory = LoggerFactory.Create(_ => { });
            Assert.Throws<ArgumentOutOfRangeException>(() => new QueuedHostedService(new BackgroundTaskQueue(), loggerFactory, configuration));
        }

        private static void VerifyErrorCount(Mock<ILogger<QueuedHostedService>> logger, Times times)
        {
            logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, type) => true), It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);
        }
    }
}
