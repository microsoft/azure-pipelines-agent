// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.Framework.Common;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Microsoft.VisualStudio.Services.Agent.Util;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Agent.Sdk;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Container
{
    public sealed class DockerCommandManagerL0
    {
        private readonly Mock<IProcessInvoker> _processInvoker;
        private readonly Mock<IExecutionContext> _ec;
        private readonly Mock<IConfigurationStore> _configurationStore;
        private readonly Mock<IJobServerQueue> _jobServerQueue;

        public DockerCommandManagerL0()
        {
            _processInvoker = new Mock<IProcessInvoker>();
            _ec = new Mock<IExecutionContext>();
            _configurationStore = new Mock<IConfigurationStore>();
            _jobServerQueue = new Mock<IJobServerQueue>();
            
            // Setup basic configuration store mocks
            _configurationStore.Setup(x => x.IsConfigured()).Returns(true);
            _configurationStore.Setup(x => x.GetSettings()).Returns(new AgentSettings());
        }

        private DockerCommandManager CreateDockerCommandManager()
        {
            var dockerManager = new DockerCommandManager();
            
            var processInvokerProperty = typeof(DockerCommandManager)
                .GetField("_processInvoker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            processInvokerProperty?.SetValue(dockerManager, _processInvoker.Object);
            
            return dockerManager;
        }

        private void SetupDockerPsForRunningContainer(string containerId)
        {
            Console.WriteLine($"[TEST SETUP] Setting up container '{containerId}' state: RUNNING");
            
            // Mock the ExecuteAsync call for docker ps
            _processInvoker.Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),                    // workingDirectory
                It.IsAny<string>(),                    // fileName
                It.Is<string>(args => args.Contains("ps") && args.Contains(containerId)), // arguments
                It.IsAny<IDictionary<string, string>>(), // environment
                It.IsAny<bool>(),                      // requireExitCodeZero
                It.IsAny<System.Text.Encoding>(),      // outputEncoding
                It.IsAny<CancellationToken>()))        // cancellationToken
                .Callback<string, string, string, IDictionary<string, string>, bool, System.Text.Encoding, CancellationToken>(
                    (workDir, fileName, args, env, requireZero, encoding, token) =>
                    {
                        // Simulate docker ps output for running container (header + container line = 2 lines)
                        _processInvoker.Raise(x => x.OutputDataReceived += null,
                            _processInvoker.Object,
                            new ProcessDataReceivedEventArgs("CONTAINER ID   IMAGE     COMMAND   CREATED   STATUS    PORTS     NAMES"));
                        _processInvoker.Raise(x => x.OutputDataReceived += null,
                            _processInvoker.Object,
                            new ProcessDataReceivedEventArgs($"{containerId}   test-image   \"test\"   1 min ago   Up 1 min   0.0.0.0:8080->80/tcp   test-container"));
                    })
                .ReturnsAsync(0);
        }

        private void SetupDockerPsForStoppedContainer(string containerId)
        {
            Console.WriteLine($"[TEST SETUP] Setting up container '{containerId}' state: STOPPED");
            
            // Mock the ExecuteAsync call for docker ps
            _processInvoker.Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),                    // workingDirectory
                It.IsAny<string>(),                    // fileName
                It.Is<string>(args => args.Contains("ps") && args.Contains(containerId)), // arguments
                It.IsAny<IDictionary<string, string>>(), // environment
                It.IsAny<bool>(),                      // requireExitCodeZero
                It.IsAny<System.Text.Encoding>(),      // outputEncoding
                It.IsAny<CancellationToken>()))        // cancellationToken
                .Callback<string, string, string, IDictionary<string, string>, bool, System.Text.Encoding, CancellationToken>(
                    (workDir, fileName, args, env, requireZero, encoding, token) =>
                    {
                        // Simulate docker ps output for stopped container (header only = 1 line)
                        _processInvoker.Raise(x => x.OutputDataReceived += null,
                            _processInvoker.Object,
                            new ProcessDataReceivedEventArgs("CONTAINER ID   IMAGE     COMMAND   CREATED   STATUS    PORTS     NAMES"));
                    })
                .ReturnsAsync(0);
        }

        private void SetupEnvironmentVariables(string var1, string var2)
        {
            var environment = new SystemEnvironment();
            environment.SetEnvironmentVariable("VSTSAGENT_DOCKER_ACTION_RETRIES", var1);
            environment.SetEnvironmentVariable("AGENT_CHECK_BEFORE_RETRY_DOCKER_START", var2);
            _ec.Setup(x => x.GetScopedEnvironment()).Returns(environment);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DockerStart_WithCheckBeforeRetryFalse_UsesStandardRetryLogic()
        {
            // Arrange
            var containerId = "test-container-id";
            var exitCode = 0;

            using (var hc = new TestHostContext(this))
            {
                var dockerManager = CreateDockerCommandManager();
                hc.SetSingleton<IConfigurationStore>(_configurationStore.Object);
                hc.SetSingleton<IJobServerQueue>(_jobServerQueue.Object);
                dockerManager.Initialize(hc);

                for (int i = 0; i < 10; i++)
                {
                    hc.EnqueueInstance<IProcessInvoker>(_processInvoker.Object);
                }
                // Setup environment variables using helper method
                SetupEnvironmentVariables("true", "false");

                // Setup process invoker to return success
                _processInvoker.Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<string>(args => args.Contains("start") && args.Contains(containerId)),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<bool>(),
                    It.IsAny<InputQueue<string>>(),
                    It.IsAny<CancellationToken>()))
                    .ReturnsAsync(exitCode);

                // Act
                var result = await dockerManager.DockerStart(_ec.Object, containerId);

                // Assert
                Assert.Equal(exitCode, result);
                _processInvoker.Verify(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<bool>(),
                    It.IsAny<InputQueue<string>>(),
                    It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DockerStart_WithCheckBeforeRetryTrue_ContainerAlreadyRunning_ReturnsSuccess()
        {
            // Arrange
            var containerId = "test-container-id";

            using (var hc = new TestHostContext(this))
            {
                var dockerManager = CreateDockerCommandManager();
                hc.SetSingleton<IConfigurationStore>(_configurationStore.Object);
                hc.SetSingleton<IJobServerQueue>(_jobServerQueue.Object);
                dockerManager.Initialize(hc);

                for (int i = 0; i < 10; i++)
                {
                    hc.EnqueueInstance<IProcessInvoker>(_processInvoker.Object);
                }

                // Setup environment variables using helper method
                SetupEnvironmentVariables("true", "true");

                // Setup process invoker for docker ps to indicate container is running (2 lines)
                SetupDockerPsForRunningContainer(containerId);

                // Act
                var result = await dockerManager.DockerStart(_ec.Object, containerId);

                // Assert
                Assert.Equal(0, result);
                
                // Verify docker ps was called but docker start was not called since container was already running
                _processInvoker.Verify(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<string>(args => args.Contains("ps")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<CancellationToken>()), Times.AtLeastOnce);
                
                _processInvoker.Verify(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<string>(args => args.Contains("start")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<bool>(),
                    It.IsAny<InputQueue<string>>(),
                    It.IsAny<CancellationToken>()), Times.Never);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DockerStart_WithCheckBeforeRetryTrue_StartSucceedsFirstAttempt_ReturnsSuccess()
        {
            // Arrange
            var containerId = "test-container-id";

            using (var hc = new TestHostContext(this))
            {
                var dockerManager = CreateDockerCommandManager();
                hc.SetSingleton<IConfigurationStore>(_configurationStore.Object);
                hc.SetSingleton<IJobServerQueue>(_jobServerQueue.Object);
                dockerManager.Initialize(hc);

                for (int i = 0; i < 10; i++)
                {
                    hc.EnqueueInstance<IProcessInvoker>(_processInvoker.Object);
                }
                // Setup environment variables using helper method
                SetupEnvironmentVariables("true", "true");

                // Setup process invoker for docker ps to indicate container is NOT running initially
                SetupDockerPsForStoppedContainer(containerId);

                // Setup process invoker for docker start to succeed
                _processInvoker.Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<string>(args => args.Contains("start") && args.Contains(containerId)),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<bool>(),
                    It.IsAny<InputQueue<string>>(),
                    It.IsAny<CancellationToken>()))
                    .ReturnsAsync(0);

                // Act
                var result = await dockerManager.DockerStart(_ec.Object, containerId);

                // Assert
                Assert.Equal(0, result);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DockerStart_WithCheckBeforeRetryTrue_AllRetriesFail_ReturnsFailure()
        {
            // Arrange
            var containerId = "test-container-id";

            using (var hc = new TestHostContext(this))
            {
                var dockerManager = CreateDockerCommandManager();
                hc.SetSingleton<IConfigurationStore>(_configurationStore.Object);
                hc.SetSingleton<IJobServerQueue>(_jobServerQueue.Object);
                dockerManager.Initialize(hc);

                for (int i = 0; i < 10; i++)
                {
                    hc.EnqueueInstance<IProcessInvoker>(_processInvoker.Object);
                }
                // Setup environment variables using helper method
                SetupEnvironmentVariables("true", "true");

                // Setup process invoker for docker ps to always indicate container is NOT running
                SetupDockerPsForStoppedContainer(containerId);

                // Setup process invoker for docker start to always fail
                _processInvoker.Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<string>(args => args.Contains("start") && args.Contains(containerId)),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<bool>(),
                    It.IsAny<InputQueue<string>>(),
                    It.IsAny<CancellationToken>()))
                    .ReturnsAsync(1); // Always fail

                // Act
                var result = await dockerManager.DockerStart(_ec.Object, containerId);

                // Assert
                Assert.Equal(1, result);
                
                // Verify docker start was called multiple times (retries)
                _processInvoker.Verify(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<string>(args => args.Contains("start")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<bool>(),
                    It.IsAny<InputQueue<string>>(),
                    It.IsAny<CancellationToken>()), Times.Exactly(3));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DockerStart_WithCheckBeforeRetryTrue_NoRetriesEnabled_FailsImmediately()
        {
            // Arrange
            var containerId = "test-container-id";

            using (var hc = new TestHostContext(this))
            {
                var dockerManager = CreateDockerCommandManager();
                hc.SetSingleton<IConfigurationStore>(_configurationStore.Object);
                hc.SetSingleton<IJobServerQueue>(_jobServerQueue.Object);
                dockerManager.Initialize(hc);

                for (int i = 0; i < 10; i++)
                {
                    hc.EnqueueInstance<IProcessInvoker>(_processInvoker.Object);
                }
                // Setup environment variables using helper method - retries disabled
                SetupEnvironmentVariables("false", "true");

                // Setup process invoker for docker ps to indicate container is NOT running
                SetupDockerPsForStoppedContainer(containerId);

                // Setup process invoker for docker start to fail
                _processInvoker.Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<string>(args => args.Contains("start") && args.Contains(containerId)),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<bool>(),
                    It.IsAny<InputQueue<string>>(),
                    It.IsAny<CancellationToken>()))
                    .ReturnsAsync(1);

                // Act
                var result = await dockerManager.DockerStart(_ec.Object, containerId);

                // Assert
                Assert.Equal(1, result);
                
                // Should only attempt docker start once (no retries)
                _processInvoker.Verify(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<string>(args => args.Contains("start")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<bool>(),
                    It.IsAny<InputQueue<string>>(),
                    It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DockerStart_WithCheckBeforeRetryTrue_RetriesWithBackoff()
        {
            // Arrange
            var containerId = "test-container-id";

            using (var hc = new TestHostContext(this))
            {
                var dockerManager = CreateDockerCommandManager();
                hc.SetSingleton<IConfigurationStore>(_configurationStore.Object);
                hc.SetSingleton<IJobServerQueue>(_jobServerQueue.Object);
                dockerManager.Initialize(hc);
                
                for (int i = 0; i < 10; i++)
                {
                    hc.EnqueueInstance<IProcessInvoker>(_processInvoker.Object);
                }
                // Setup environment variables using helper method
                SetupEnvironmentVariables("true", "true");

                // Setup process invoker for docker ps to indicate container is NOT running
                SetupDockerPsForStoppedContainer(containerId);

                var startCallCount = 0;
                // Setup process invoker for docker start to fail twice, then succeed
                _processInvoker.Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<string>(args => args.Contains("start") && args.Contains(containerId)),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<bool>(),
                    It.IsAny<InputQueue<string>>(),
                    It.IsAny<CancellationToken>()))
                    .Callback(() => startCallCount++)
                    .ReturnsAsync(() => startCallCount <= 2 ? 1 : 0); // Fail twice, then succeed

                // Act
                var result = await dockerManager.DockerStart(_ec.Object, containerId);

                // Assert
                Assert.Equal(0, result);
                
                // Verify docker start was called multiple times
                _processInvoker.Verify(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<string>(args => args.Contains("start")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<bool>(),
                    It.IsAny<InputQueue<string>>(),
                    It.IsAny<CancellationToken>()), Times.Exactly(3));
            }
        }
    }
}
