// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.Framework.Common;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Microsoft.VisualStudio.Services.Agent.Util;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
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
        private readonly Mock<IHostContext> _hostContext;

        public DockerCommandManagerL0()
        {
            _processInvoker = new Mock<IProcessInvoker>();
            _ec = new Mock<IExecutionContext>();
            _configurationStore = new Mock<IConfigurationStore>();
            _jobServerQueue = new Mock<IJobServerQueue>();
            _hostContext = new Mock<IHostContext>();
            
            // Setup basic host context functionality
            _hostContext.Setup(x => x.GetTrace(It.IsAny<string>())).Returns((Tracing)null);
            
            // Setup basic configuration store mocks
            _configurationStore.Setup(x => x.IsConfigured()).Returns(true);
            _configurationStore.Setup(x => x.GetSettings()).Returns(new AgentSettings());
        }

        private bool IsDockerAvailable()
        {
            // Check if Docker is available
            try
            {
                WhichUtil.Which("docker", true);
                return true;
            }
            catch (FileNotFoundException)
            {
                // Docker not available
                return false;
            }
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

        private void SetupEnvironmentVariables(string dockerActionRetries, string checkBeforeRetryDockerStart)
        {
            var environment = new SystemEnvironment();
            environment.SetEnvironmentVariable("VSTSAGENT_DOCKER_ACTION_RETRIES", dockerActionRetries);
            environment.SetEnvironmentVariable("AGENT_CHECK_BEFORE_RETRY_DOCKER_START", checkBeforeRetryDockerStart);
            _ec.Setup(x => x.GetScopedEnvironment()).Returns(environment);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        public async Task DockerStart_WithCheckBeforeRetryFalse_UsesStandardRetryLogic()
        {
            if (!IsDockerAvailable()) return;
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
        [Trait("SkipOn", "darwin")]
        public async Task DockerStart_WithCheckBeforeRetryTrue_ContainerAlreadyRunning_ReturnsSuccess()
        {
            if (!IsDockerAvailable()) return;
            
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
        [Trait("SkipOn", "darwin")]
        public async Task DockerStart_WithCheckBeforeRetryTrue_StartSucceedsFirstAttempt_ReturnsSuccess()
        {
            if (!IsDockerAvailable()) return;
            
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
        [Trait("SkipOn", "darwin")]
        public async Task DockerStart_WithCheckBeforeRetryTrue_AllRetriesFail_ReturnsFailure()
        {
            if (!IsDockerAvailable()) return;
            
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
        [Trait("SkipOn", "darwin")]
        public async Task DockerStart_WithCheckBeforeRetryTrue_NoRetriesEnabled_FailsImmediately()
        {
            if (!IsDockerAvailable()) return;
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
        [Trait("SkipOn", "darwin")]
        public async Task DockerStart_WithCheckBeforeRetryTrue_RetriesWithBackoff()
        {
            if (!IsDockerAvailable()) return;
            
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

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void FormatMountVolumeArg_BindMount_BuildsExpectedArg()
        {
            Assert.Equal(
                "-v \"C:\\src\":\"C:\\__w\"",
                DockerCommandManager.FormatMountVolumeArg("C:\\src", "C:\\__w", readOnly: false));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void FormatMountVolumeArg_BindMount_ReadOnly_AppendsRoSuffix()
        {
            Assert.Equal(
                "-v \"C:\\src\":\"C:\\__w\":ro",
                DockerCommandManager.FormatMountVolumeArg("C:\\src", "C:\\__w", readOnly: true));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void FormatMountVolumeArg_AnonymousVolume_NoSource()
        {
            Assert.Equal(
                "-v \"/var/data\"",
                DockerCommandManager.FormatMountVolumeArg(null, "/var/data", readOnly: false));

            Assert.Equal(
                "-v \"/var/data\"",
                DockerCommandManager.FormatMountVolumeArg(string.Empty, "/var/data", readOnly: false));
        }

        // Regression: a Windows drive-root source like "F:\" used to produce -v "F:\":"C:\__w",
        // which the Windows C runtime parses as a single argument (the trailing backslash
        // escapes the closing quote), causing docker to fail with "too many colons". Trailing
        // backslashes must be doubled so the closing quote is preserved.
        // Windows-only: on Linux/macOS '\' is a literal path character and is not doubled.
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public void FormatMountVolumeArg_BindMount_DriveRootSource_DoublesTrailingBackslash()
        {
            Assert.Equal(
                "-v \"F:\\\\\":\"C:\\__w\"",
                DockerCommandManager.FormatMountVolumeArg("F:\\", "C:\\__w", readOnly: false));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public void FormatMountVolumeArg_BindMount_DriveRootTarget_DoublesTrailingBackslash()
        {
            Assert.Equal(
                "-v \"C:\\src\":\"D:\\\\\"",
                DockerCommandManager.FormatMountVolumeArg("C:\\src", "D:\\", readOnly: false));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public void FormatMountVolumeArg_BindMount_MultipleTrailingBackslashes_AreAllDoubled()
        {
            Assert.Equal(
                "-v \"F:\\\\\\\\\":\"C:\\__w\"",
                DockerCommandManager.FormatMountVolumeArg("F:\\\\", "C:\\__w", readOnly: false));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public void FormatMountVolumeArg_AnonymousVolume_TrailingBackslashTarget_IsDoubled()
        {
            Assert.Equal(
                "-v \"C:\\data\\\\\"",
                DockerCommandManager.FormatMountVolumeArg(null, "C:\\data\\", readOnly: false));
        }

        // Non-Windows counterpart: on Linux/macOS, a trailing backslash is a literal path
        // character (not a separator) and must be left untouched by the quoting helper.
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "windows")]
        public void FormatMountVolumeArg_BindMount_TrailingBackslash_NotDoubledOnNonWindows()
        {
            if (PlatformUtil.RunningOnWindows)
            {
                // SkipOn=windows in CI; guard here so local Windows runs don't fail.
                return;
            }

            Assert.Equal(
                "-v \"/mnt/weird\\\":\"/container\\\"",
                DockerCommandManager.FormatMountVolumeArg("/mnt/weird\\", "/container\\", readOnly: false));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void FormatMountVolumeArg_BindMount_PathWithEmbeddedQuote_IsEscaped()
        {
            Assert.Equal(
                "-v \"C:\\weird\\\"name\":\"/mnt/x\"",
                DockerCommandManager.FormatMountVolumeArg("C:\\weird\"name", "/mnt/x", readOnly: false));
        }

        // Regression: exact mount specs observed in the InitializeContainers step logs on a
        // Windows agent. All four must produce arguments that docker.exe parses as a single
        // valid -v spec (the F:\ case used to fail with "invalid spec ... too many colons").
        // Windows-only because the expected output reflects the trailing-backslash doubling.
        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        [InlineData("D:\\a\\_work", "C:\\__w", "-v \"D:\\a\\_work\":\"C:\\__w\"")]
        [InlineData("D:\\a\\_work\\_tasks", "C:\\__w\\_tasks", "-v \"D:\\a\\_work\\_tasks\":\"C:\\__w\\_tasks\"")]
        [InlineData("F:\\", "C:\\__w", "-v \"F:\\\\\":\"C:\\__w\"")]
        [InlineData("F:\\_tasks", "F:\\_tasks", "-v \"F:\\_tasks\":\"F:\\_tasks\"")]
        public void FormatMountVolumeArg_InitializeContainersLogScenarios(string source, string target, string expected)
        {
            Assert.Equal(expected, DockerCommandManager.FormatMountVolumeArg(source, target, readOnly: false));
        }
    }
}
