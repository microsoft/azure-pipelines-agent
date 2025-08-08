using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Xunit;
using Moq;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Container
{
    public class DockerCommandManagerL0Tests
    {
        private Mock<IExecutionContext> _ec;

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "worker.container")]
        public async Task ReturnsZeroIfContainerAlreadyRunningBeforeStart()
        {
            _ec = new Mock<IExecutionContext>();
            SetupEnvironmentVariables("true");
            var manager = new TestableDockerCommandManager(isRunningOnFirstCheck: true);
            int result = await manager.ExecuteDockerStartWithRetriesAndCheckPublic(_ec.Object, "cid");
            Assert.Equal(0, result);
            Assert.Equal(1, manager.IsContainerRunningCallCount);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "worker.container")]
        public async Task ReturnsZeroIfStartSucceedsFirstTry()
        {
            _ec = new Mock<IExecutionContext>();
            SetupEnvironmentVariables("true");
            var manager = new TestableDockerCommandManager(exitCodes: new[] { 0 }, runningOnRetry: new[] { false, true });
            int result = await manager.ExecuteDockerStartWithRetriesAndCheckPublic(_ec.Object, "cid");
            Assert.Equal(0, result);
            Assert.Equal(1, manager.IsContainerRunningCallCount);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "worker.container")]
        public async Task ReturnsZeroIfContainerStartsOnThirdRetry()
        {
            _ec = new Mock<IExecutionContext>();
            SetupEnvironmentVariables("true");
            var manager = new TestableDockerCommandManager(exitCodes: new[] { 1, 1, 0 }, runningOnRetry: new[] { false, false, false, true });
            int result = await manager.ExecuteDockerStartWithRetriesAndCheckPublic(_ec.Object, "cid");
            Assert.Equal(0, result);
            Assert.Equal(3, manager.IsContainerRunningCallCount);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "worker.container")]
        public async Task ReturnsExitCodeIfContainerNeverStarts()
        {
            _ec = new Mock<IExecutionContext>();
            SetupEnvironmentVariables("true");
            var manager = new TestableDockerCommandManager(exitCodes: new[] { 1, 2, 3 }, runningOnRetry: new[] { false, false, false, false });
            int result = await manager.ExecuteDockerStartWithRetriesAndCheckPublic(_ec.Object, "cid");
            Assert.Equal(3, result);
            Assert.Equal(4, manager.IsContainerRunningCallCount);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "worker.container")]
        public async Task ReturnsZeroIfContainerStartsButExitCodeNotZero()
        {
            _ec = new Mock<IExecutionContext>();
            SetupEnvironmentVariables("true");
            // exitCode is 1, but container is running after
            var manager = new TestableDockerCommandManager(exitCodes: new[] { 1 }, runningOnRetry: new[] { false, true });
            int result = await manager.ExecuteDockerStartWithRetriesAndCheckPublic(_ec.Object, "cid");
            Assert.Equal(0, result);
            Assert.Equal(3, manager.IsContainerRunningCallCount);
        }

        private class TestableDockerCommandManager : DockerCommandManager
        {
            private readonly int[] _exitCodes;
            private readonly bool[] _runningOnRetry;
            private int _startCallCount = 0;
            private int _runningCallCount = 0;
            public int IsContainerRunningCallCount => _runningCallCount;

            public TestableDockerCommandManager(bool isRunningOnFirstCheck = false)
            {
                _exitCodes = new[] { 1 };
                _runningOnRetry = new[] { isRunningOnFirstCheck };
            }
            public TestableDockerCommandManager(int[] exitCodes, bool[] runningOnRetry)
            {
                _exitCodes = exitCodes;
                _runningOnRetry = runningOnRetry;
            }
            public Task<int> ExecuteDockerStartWithRetriesAndCheckPublic(IExecutionContext context, string containerId)
            {
                return base.ExecuteDockerStartWithRetriesAndCheck(context, containerId);
            }
            protected override Task<int> ExecuteDockerCommandAsync(IExecutionContext context, string command, string options, CancellationToken cancellationToken = default)
            {
                int code = _exitCodes[Math.Min(_startCallCount, _exitCodes.Length - 1)];
                _startCallCount++;
                return Task.FromResult(code);
            }
            public override Task<bool> IsContainerRunning(IExecutionContext context, string containerId)
            {
                bool running = _runningOnRetry[Math.Min(_runningCallCount, _runningOnRetry.Length - 1)];
                _runningCallCount++;
                return Task.FromResult(running);
            }
        }

        private void SetupEnvironmentVariables(string allowDockerActionRetries)
        {
            var environment = new SystemEnvironment();
            environment.SetEnvironmentVariable("VSTSAGENT_DOCKER_ACTION_RETRIES", allowDockerActionRetries);
            _ec.Setup(x => x.GetScopedEnvironment()).Returns(environment);
        }
    }
}
