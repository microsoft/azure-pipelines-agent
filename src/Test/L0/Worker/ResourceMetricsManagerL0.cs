// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class ResourceMetricsManagerL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task ReportsMemoryWarningOnceToEachAttachedTask()
        {
            using var hc = new TestHostContext(this);
            using var jobCancellation = new CancellationTokenSource();
            var provider = new TestResourceMetricsProvider
            {
                MemoryInfo = new ResourceMemoryInfo(totalMemoryMB: 100, usedMemoryMB: 96),
            };
            using var manager = CreateManager(hc, provider);
            var firstWarning = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondWarning = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstContext = CreateTaskContext(firstWarning);
            var secondContext = CreateTaskContext(secondWarning);

            manager.StartMonitoring(CreateJobContext(jobCancellation.Token).Object, enableDebugOutput: false);

            using (manager.AttachTask(firstContext.Object, Guid.NewGuid(), enableWarnings: true))
            {
                await firstWarning.Task.WaitAsync(TimeSpan.FromSeconds(30));
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                firstContext.Verify(x => x.Write(WellKnownTags.Warning, It.IsAny<string>(), true), Times.Once);
                firstContext.Verify(x => x.AddIssue(It.IsAny<Issue>()), Times.Never);
            }

            using (manager.AttachTask(secondContext.Object, Guid.NewGuid(), enableWarnings: true))
            {
                await secondWarning.Task.WaitAsync(TimeSpan.FromSeconds(30));
                secondContext.Verify(x => x.Write(WellKnownTags.Warning, It.IsAny<string>(), true), Times.Once);
            }

            await manager.StopMonitoringAsync();
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DetachedTaskDoesNotReceiveCompletedSample()
        {
            using var hc = new TestHostContext(this);
            using var jobCancellation = new CancellationTokenSource();
            var sampleStarted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSample = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new TestResourceMetricsProvider
            {
                GetMemoryInfoAsyncCallback = async cancellationToken =>
                {
                    sampleStarted.TrySetResult(0);
                    await releaseSample.Task.WaitAsync(cancellationToken);
                    return new ResourceMemoryInfo(totalMemoryMB: 100, usedMemoryMB: 96);
                },
            };
            using var manager = CreateManager(hc, provider);
            var taskContext = new Mock<IExecutionContext>();

            manager.StartMonitoring(CreateJobContext(jobCancellation.Token).Object, enableDebugOutput: false);

            IDisposable taskScope = manager.AttachTask(taskContext.Object, Guid.NewGuid(), enableWarnings: true);
            await sampleStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
            taskScope.Dispose();
            releaseSample.TrySetResult(0);
            await Task.Delay(TimeSpan.FromMilliseconds(100));

            taskContext.Verify(x => x.Write(WellKnownTags.Warning, It.IsAny<string>(), true), Times.Never);
            await manager.StopMonitoringAsync();
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task CompletedSampleIsNotReportedToNextTask()
        {
            using var hc = new TestHostContext(this);
            using var jobCancellation = new CancellationTokenSource();
            var sampleStarted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSample = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            int sampleCount = 0;
            var provider = new TestResourceMetricsProvider
            {
                GetMemoryInfoAsyncCallback = async cancellationToken =>
                {
                    if (Interlocked.Increment(ref sampleCount) == 1)
                    {
                        sampleStarted.TrySetResult(0);
                        await releaseSample.Task.WaitAsync(cancellationToken);
                        return new ResourceMemoryInfo(totalMemoryMB: 100, usedMemoryMB: 96);
                    }

                    return new ResourceMemoryInfo(totalMemoryMB: 100, usedMemoryMB: 50);
                },
            };
            using var manager = CreateManager(hc, provider);
            var firstContext = new Mock<IExecutionContext>();
            var secondContext = new Mock<IExecutionContext>();

            manager.StartMonitoring(CreateJobContext(jobCancellation.Token).Object, enableDebugOutput: false);

            IDisposable firstScope = manager.AttachTask(firstContext.Object, Guid.NewGuid(), enableWarnings: true);
            await sampleStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
            firstScope.Dispose();

            using (manager.AttachTask(secondContext.Object, Guid.NewGuid(), enableWarnings: true))
            {
                releaseSample.TrySetResult(0);
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }

            firstContext.Verify(x => x.Write(WellKnownTags.Warning, It.IsAny<string>(), true), Times.Never);
            secondContext.Verify(x => x.Write(WellKnownTags.Warning, It.IsAny<string>(), true), Times.Never);
            await manager.StopMonitoringAsync();
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DetachWaitsForInFlightTaskLogWrite()
        {
            using var hc = new TestHostContext(this);
            using var jobCancellation = new CancellationTokenSource();
            using var warningEntered = new ManualResetEventSlim();
            using var releaseWarning = new ManualResetEventSlim();
            var provider = new TestResourceMetricsProvider
            {
                MemoryInfo = new ResourceMemoryInfo(totalMemoryMB: 100, usedMemoryMB: 96),
            };
            using var manager = CreateManager(hc, provider);
            var taskContext = new Mock<IExecutionContext>();
            taskContext.Setup(x => x.Write(WellKnownTags.Warning, It.IsAny<string>(), true)).Callback(() =>
            {
                warningEntered.Set();
                releaseWarning.Wait();
            }).Returns(1);

            manager.StartMonitoring(CreateJobContext(jobCancellation.Token).Object, enableDebugOutput: false);
            IDisposable taskScope = manager.AttachTask(taskContext.Object, Guid.NewGuid(), enableWarnings: true);
            Assert.True(warningEntered.Wait(TimeSpan.FromSeconds(30)));

            Task detachTask = Task.Run(() => taskScope.Dispose());
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            Assert.False(detachTask.IsCompleted);

            releaseWarning.Set();
            await detachTask.WaitAsync(TimeSpan.FromSeconds(30));
            await manager.StopMonitoringAsync();
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task StopMonitoringDoesNotWaitIndefinitelyForCollection()
        {
            using var hc = new TestHostContext(this);
            using var jobCancellation = new CancellationTokenSource();
            var sampleStarted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSample = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sampleCompleted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new TestResourceMetricsProvider
            {
                GetMemoryInfoAsyncCallback = async cancellationToken =>
                {
                    sampleStarted.TrySetResult(0);
                    await releaseSample.Task;
                    sampleCompleted.TrySetResult(0);
                    return new ResourceMemoryInfo(totalMemoryMB: 100, usedMemoryMB: 50);
                },
            };
            using var manager = CreateManager(hc, provider);
            manager.MonitorStopTimeout = TimeSpan.FromMilliseconds(20);

            manager.StartMonitoring(CreateJobContext(jobCancellation.Token).Object, enableDebugOutput: false);
            using (manager.AttachTask(new Mock<IExecutionContext>().Object, Guid.NewGuid(), enableWarnings: true))
            {
                await sampleStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
                await manager.StopMonitoringAsync().WaitAsync(TimeSpan.FromSeconds(1));
            }

            releaseSample.TrySetResult(0);
            await sampleCompleted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task CollectionFailureDoesNotStopMonitoring()
        {
            using var hc = new TestHostContext(this);
            using var jobCancellation = new CancellationTokenSource();
            int attempts = 0;
            var provider = new TestResourceMetricsProvider
            {
                GetMemoryInfoAsyncCallback = cancellationToken =>
                {
                    if (Interlocked.Increment(ref attempts) == 1)
                    {
                        throw new InvalidOperationException("collection failed");
                    }

                    return Task.FromResult(new ResourceMemoryInfo(totalMemoryMB: 100, usedMemoryMB: 96));
                },
            };
            using var manager = CreateManager(hc, provider);
            var warning = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var taskContext = CreateTaskContext(warning);

            manager.StartMonitoring(CreateJobContext(jobCancellation.Token).Object, enableDebugOutput: false);
            using (manager.AttachTask(taskContext.Object, Guid.NewGuid(), enableWarnings: true))
            {
                await warning.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }

            Assert.True(Volatile.Read(ref attempts) >= 2);
            await manager.StopMonitoringAsync();
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task TaskLoggingFailureDoesNotStopMonitoring()
        {
            using var hc = new TestHostContext(this);
            using var jobCancellation = new CancellationTokenSource();
            var provider = new TestResourceMetricsProvider
            {
                MemoryInfo = new ResourceMemoryInfo(totalMemoryMB: 100, usedMemoryMB: 96),
            };
            using var manager = CreateManager(hc, provider);
            var failedWrite = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failingContext = new Mock<IExecutionContext>();
            failingContext.Setup(x => x.Write(WellKnownTags.Warning, It.IsAny<string>(), true)).Callback(() =>
            {
                failedWrite.TrySetResult(0);
                throw new InvalidOperationException("logging failed");
            });
            var successfulWrite = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var successfulContext = CreateTaskContext(successfulWrite);

            manager.StartMonitoring(CreateJobContext(jobCancellation.Token).Object, enableDebugOutput: false);

            using (manager.AttachTask(failingContext.Object, Guid.NewGuid(), enableWarnings: true))
            {
                await failedWrite.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }

            using (manager.AttachTask(successfulContext.Object, Guid.NewGuid(), enableWarnings: true))
            {
                await successfulWrite.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }

            await manager.StopMonitoringAsync();
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DisabledTaskDoesNotCollectWarningMetrics()
        {
            using var hc = new TestHostContext(this);
            using var jobCancellation = new CancellationTokenSource();
            var provider = new TestResourceMetricsProvider();
            using var manager = CreateManager(hc, provider);
            var taskContext = new Mock<IExecutionContext>();

            manager.StartMonitoring(CreateJobContext(jobCancellation.Token).Object, enableDebugOutput: false);
            using (manager.AttachTask(taskContext.Object, Guid.NewGuid(), enableWarnings: false))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }

            Assert.Equal(0, provider.MemoryCalls);
            taskContext.Verify(x => x.Write(WellKnownTags.Warning, It.IsAny<string>(), true), Times.Never);
            await manager.StopMonitoringAsync();
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DebugOutputIsWrittenToAttachedTask()
        {
            using var hc = new TestHostContext(this);
            using var jobCancellation = new CancellationTokenSource();
            var provider = new TestResourceMetricsProvider();
            using var manager = CreateManager(hc, provider);
            var debugWrite = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var taskContext = new Mock<IExecutionContext>();
            taskContext.SetupGet(x => x.WriteDebug).Returns(true);
            taskContext.Setup(x => x.Write(WellKnownTags.Debug, It.IsAny<string>(), true))
                .Callback(() => debugWrite.TrySetResult(0))
                .Returns(1);

            manager.StartMonitoring(CreateJobContext(jobCancellation.Token).Object, enableDebugOutput: true);
            using (manager.AttachTask(taskContext.Object, Guid.NewGuid(), enableWarnings: false))
            {
                await debugWrite.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }

            taskContext.Verify(x => x.Write(WellKnownTags.Debug, It.IsAny<string>(), true), Times.AtLeastOnce);
            await manager.StopMonitoringAsync();
        }

        private static ResourceMetricsManager CreateManager(TestHostContext hc, ResourceMetricsProviderBase provider)
        {
            var manager = new ResourceMetricsManager
            {
                MetricsProvider = provider,
                MonitorInterval = TimeSpan.FromMilliseconds(20),
                MetricsTimeout = TimeSpan.FromSeconds(5),
            };
            manager.Initialize(hc);
            return manager;
        }

        private static Mock<IExecutionContext> CreateJobContext(CancellationToken cancellationToken)
        {
            var context = new Mock<IExecutionContext>();
            context.SetupGet(x => x.CancellationToken).Returns(cancellationToken);
            context.Setup(x => x.GetVariableValueOrDefault(Constants.Variables.Agent.WorkFolder)).Returns("_work");
            return context;
        }

        private static Mock<IExecutionContext> CreateTaskContext(TaskCompletionSource<int> warning)
        {
            var context = new Mock<IExecutionContext>();
            context.Setup(x => x.Write(WellKnownTags.Warning, It.IsAny<string>(), true))
                .Callback(() => warning.TrySetResult(0))
                .Returns(1);
            return context;
        }

        private sealed class TestResourceMetricsProvider : ResourceMetricsProviderBase
        {
            private int _memoryCalls;

            public ResourceCpuInfo CpuInfo { get; set; } = new ResourceCpuInfo(usage: 10);
            public ResourceDiskInfo DiskInfo { get; set; } = new ResourceDiskInfo("C:\\", totalDiskSpaceMB: 100, freeDiskSpaceMB: 50);
            public ResourceMemoryInfo MemoryInfo { get; set; } = new ResourceMemoryInfo(totalMemoryMB: 100, usedMemoryMB: 50);
            public Func<CancellationToken, Task<ResourceMemoryInfo>> GetMemoryInfoAsyncCallback { get; set; }
            public int MemoryCalls => Volatile.Read(ref _memoryCalls);

            public override Task<ResourceCpuInfo> GetCpuInfoAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(CpuInfo);
            }

            public override ResourceDiskInfo GetDiskInfo(string workFolder)
            {
                return DiskInfo;
            }

            public override Task<ResourceMemoryInfo> GetMemoryInfoAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _memoryCalls);
                return GetMemoryInfoAsyncCallback?.Invoke(cancellationToken) ?? Task.FromResult(MemoryInfo);
            }
        }
    }
}
