// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Worker;
using Moq;
using System;
using System.Runtime.CompilerServices;
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
        public Task MemoryMonitorDoesNotStartWhileAnotherIsRunning()
        {
            return VerifyOnlyOneMonitorRuns(manager => manager.RunMemoryUtilizationMonitorAsync());
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public Task DiskMonitorDoesNotStartWhileAnotherIsRunning()
        {
            return VerifyOnlyOneMonitorRuns(manager => manager.RunDiskSpaceUtilizationMonitorAsync());
        }

        private async Task VerifyOnlyOneMonitorRuns(Func<ResourceMetricsManager, Task> startMonitor, [CallerMemberName] string testName = "")
        {
            using (var hc = new TestHostContext(this, testName))
            using (var cts = new CancellationTokenSource())
            using (var firstMonitorEntered = new ManualResetEventSlim())
            using (var releaseFirstMonitor = new ManualResetEventSlim())
            {
                // Arrange: every monitor loop reads the context's token first; hold the first one there.
                int tokenReads = 0;
                var ec = new Mock<IExecutionContext>();
                ec.SetupGet(x => x.CancellationToken).Returns(() =>
                {
                    if (Interlocked.Increment(ref tokenReads) == 1)
                    {
                        firstMonitorEntered.Set();
                        releaseFirstMonitor.Wait();
                    }

                    return cts.Token;
                });

                var manager = new ResourceMetricsManager();
                manager.Initialize(hc);
                manager.SetContext(ec.Object);

                Task firstMonitor = Task.Run(() => startMonitor(manager));
                Assert.True(firstMonitorEntered.Wait(TimeSpan.FromSeconds(30)));

                // Act.
                Task secondMonitor = startMonitor(manager);

                // Assert: the second call returns without starting a loop.
                Assert.True(secondMonitor.IsCompleted);
                Assert.Equal(1, Volatile.Read(ref tokenReads));

                cts.Cancel();
                releaseFirstMonitor.Set();
                await firstMonitor;

                // Once the first monitor has stopped, a new one can start.
                int readsBeforeRestart = Volatile.Read(ref tokenReads);
                await startMonitor(manager);
                Assert.True(Volatile.Read(ref tokenReads) > readsBeforeRestart);
            }
        }
    }
}
