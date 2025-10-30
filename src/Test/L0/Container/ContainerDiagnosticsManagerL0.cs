// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Microsoft.VisualStudio.Services.Agent.Util;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Container
{
    public sealed class ContainerDiagnosticsManagerL0
    {
        private readonly Mock<IDockerCommandManager> _dockerManager;
        private readonly Mock<IProcessInvoker> _processInvoker;

        public ContainerDiagnosticsManagerL0()
        {
            _dockerManager = new Mock<IDockerCommandManager>();
            _processInvoker = new Mock<IProcessInvoker>();
            
            _processInvoker.Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.Is<string>(args => args.Contains("inspect")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<bool>(),
                It.IsAny<System.Text.Encoding>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);
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

        private TestHostContext CreateTestContext([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
        {
            var hc = new TestHostContext(this, testName);
            
            // Register the mock docker manager as a singleton
            hc.SetSingleton(_dockerManager.Object);
            
            // Enqueue process invoker instances for the diagnostic calls
            for (int i = 0; i < 10; i++)
            {
                hc.EnqueueInstance(_processInvoker.Object);
            }
            
            return hc;
        }

        [InlineData(137, "SIGKILL")]
        [InlineData(1, "Generic failure")]
        [InlineData(127, "Command not found")]
        [InlineData(126, "Permission denied")]
        [InlineData(0, "Success")]
        [InlineData(-1073741819, "Windows error")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        public async Task CollectDockerExecFailureDiagnostics_VariousExitCodes_DoesNotThrow(int exitCode, string description)
        {
            if (!IsDockerAvailable()) return;

            using (TestHostContext hc = CreateTestContext())
            {
                // Arrange
                var diagnosticsManager = new ContainerDiagnosticsManager();
                diagnosticsManager.Initialize(hc);
                
                var dockerPath = "docker";
                var dockerArgs = $"exec -i container{exitCode} bash";
                var containerId = $"container{exitCode}";
                var exception = new ProcessExitCodeException(exitCode, "docker", dockerArgs);

                // Act & Assert - Should not throw regardless of exit code
                await diagnosticsManager.CollectDockerExecFailureDiagnosticsAsync(
                    exception,
                    dockerPath,
                    dockerArgs,
                    containerId);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        public async Task CollectDockerExecFailureDiagnostics_OperationCanceledException_DoesNotThrow()
        {
            if (!IsDockerAvailable()) return;

            using (TestHostContext hc = CreateTestContext())
            {
                // Arrange
                var diagnosticsManager = new ContainerDiagnosticsManager();
                diagnosticsManager.Initialize(hc);
                
                var dockerPath = "docker";
                var dockerArgs = "exec -i containerdef bash";
                var containerId = "containerdef";
                var exception = new OperationCanceledException("The operation was canceled");

                // Act & Assert - Should handle cancellation gracefully
                await diagnosticsManager.CollectDockerExecFailureDiagnosticsAsync(
                    exception,
                    dockerPath,
                    dockerArgs,
                    containerId);
            }
        }

        [Theory]
        [InlineData(null, "null")]
        [InlineData("", "empty")]
        [InlineData("abc123", "short")]
        [InlineData("ec520c5e3e951156a1b28bd423c3cb363ec0a4b2c97843fcec178c49b041306c", "long 64-char")]
        [InlineData("special-container_123", "special characters")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        public async Task CollectDockerExecFailureDiagnostics_VariousContainerIds_DoesNotThrow(string containerId, string description)
        {
            if (!IsDockerAvailable()) return;

            using (TestHostContext hc = CreateTestContext())
            {
                // Arrange
                var diagnosticsManager = new ContainerDiagnosticsManager();
                diagnosticsManager.Initialize(hc);
                
                var dockerPath = "docker";
                var dockerArgs = $"exec -i {containerId ?? "somecontainer"} bash";
                var exception = new ProcessExitCodeException(137, "docker", dockerArgs);

                // Act & Assert - Should handle {description} container ID gracefully
                await diagnosticsManager.CollectDockerExecFailureDiagnosticsAsync(
                    exception,
                    dockerPath,
                    dockerArgs,
                    containerId);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        public async Task CollectDockerExecFailureDiagnostics_GenericException_DoesNotThrow()
        {
            if (!IsDockerAvailable()) return;

            using (TestHostContext hc = CreateTestContext())
            {
                // Arrange
                var diagnosticsManager = new ContainerDiagnosticsManager();
                diagnosticsManager.Initialize(hc);
                
                var dockerPath = "docker";
                var dockerArgs = "exec -i container999 bash";
                var containerId = "container999";
                var exception = new Exception("Some generic error");

                // Act & Assert - Should handle generic exceptions
                await diagnosticsManager.CollectDockerExecFailureDiagnosticsAsync(
                    exception,
                    dockerPath,
                    dockerArgs,
                    containerId);
            }
        }


        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        public async Task CollectDockerExecFailureDiagnostics_VerifiesDockerInspectCalled()
        {
            if (!IsDockerAvailable()) return;

            using (TestHostContext hc = CreateTestContext())
            {
                // Arrange
                var diagnosticsManager = new ContainerDiagnosticsManager();
                diagnosticsManager.Initialize(hc);
                
                var dockerPath = "docker";
                var dockerArgs = "exec -i testcontainer bash";
                var containerId = "testcontainer";
                var exception = new ProcessExitCodeException(137, "docker", dockerArgs);

                // Act
                await diagnosticsManager.CollectDockerExecFailureDiagnosticsAsync(
                    exception,
                    dockerPath,
                    dockerArgs,
                    containerId);

                // Assert - Verify docker inspect was called for container state
                _processInvoker.Verify(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<string>(args => args.Contains("inspect") && args.Contains("State")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<CancellationToken>()),
                    Times.AtLeastOnce());

                // Assert - Verify docker inspect was called for resource state
                _processInvoker.Verify(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<string>(args => args.Contains("inspect") && args.Contains("HostConfig")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<CancellationToken>()),
                    Times.AtLeastOnce());
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        public async Task CollectDockerExecFailureDiagnostics_VerifiesDockerLogsCalled()
        {
            if (!IsDockerAvailable()) return;

            using (TestHostContext hc = CreateTestContext())
            {
                // Arrange
                var diagnosticsManager = new ContainerDiagnosticsManager();
                diagnosticsManager.Initialize(hc);
                
                var dockerPath = "docker";
                var dockerArgs = "exec -i logcontainer bash";
                var containerId = "logcontainer";
                var exception = new ProcessExitCodeException(137, "docker", dockerArgs);

                // Act
                await diagnosticsManager.CollectDockerExecFailureDiagnosticsAsync(
                    exception,
                    dockerPath,
                    dockerArgs,
                    containerId);

                // Assert - Verify docker logs was called
                _processInvoker.Verify(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<string>(args => args.Contains("logs")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<CancellationToken>()),
                    Times.AtLeastOnce());
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        public async Task CollectDockerExecFailureDiagnostics_VerifiesDockerVersionCalled()
        {
            if (!IsDockerAvailable()) return;

            using (TestHostContext hc = CreateTestContext())
            {
                // Arrange
                var diagnosticsManager = new ContainerDiagnosticsManager();
                diagnosticsManager.Initialize(hc);
                
                var dockerPath = "docker";
                var dockerArgs = "exec -i versioncontainer bash";
                var containerId = "versioncontainer";
                var exception = new ProcessExitCodeException(137, "docker", dockerArgs);

                // Act
                await diagnosticsManager.CollectDockerExecFailureDiagnosticsAsync(
                    exception,
                    dockerPath,
                    dockerArgs,
                    containerId);

                // Assert - Verify docker version was called for daemon health check
                _processInvoker.Verify(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<string>(args => args.Contains("version")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<CancellationToken>()),
                    Times.AtLeastOnce());
            }
        }

        // Scenario-based tests verifying diagnostics are collected for specific failure modes

        [Theory]
        [InlineData(137, "node script.js", "OOM killed (SIGKILL)")]
        [InlineData(127, "node --version", "Command not found")]
        [InlineData(126, "bash -c 'cat /secure/file'", "Permission denied")]
        [InlineData(1, "failing-command", "Generic docker exec failure")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        public async Task WhenDockerExecFails_DiagnosticsCollectedForScenario(int exitCode, string command, string scenario)
        {
            if (!IsDockerAvailable()) return;

            using (TestHostContext hc = CreateTestContext())
            {
                // Arrange
                var diagnosticsManager = new ContainerDiagnosticsManager();
                diagnosticsManager.Initialize(hc);
                
                var dockerPath = "docker";
                var dockerArgs = $"exec -i testcontainer {command}";
                var containerId = "testcontainer";
                var exception = new ProcessExitCodeException(exitCode, "docker", dockerArgs);

                // Act
                await diagnosticsManager.CollectDockerExecFailureDiagnosticsAsync(
                    exception,
                    dockerPath,
                    dockerArgs,
                    containerId);

                // Assert - Verify diagnostic commands were called for scenario
                _processInvoker.Verify(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<string>(args => args.Contains("inspect")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<CancellationToken>()),
                    Times.AtLeastOnce(),
                    $"Should inspect container for scenario: {scenario}");

                _processInvoker.Verify(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<string>(args => args.Contains("logs")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<CancellationToken>()),
                    Times.AtLeastOnce(),
                    $"Should collect container logs for scenario: {scenario}");

                _processInvoker.Verify(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.Is<string>(args => args.Contains("version")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<CancellationToken>()),
                    Times.AtLeastOnce(),
                    $"Should verify Docker daemon health for scenario: {scenario}");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        public async Task WhenCancellationRequested_DiagnosticsHandledGracefully()
        {
            if (!IsDockerAvailable()) return;

            using (TestHostContext hc = CreateTestContext())
            {
                // Arrange
                var diagnosticsManager = new ContainerDiagnosticsManager();
                diagnosticsManager.Initialize(hc);
                
                var dockerPath = "docker";
                var dockerArgs = "exec -i cancelcontainer long-running-command";
                var containerId = "cancelcontainer";
                var exception = new OperationCanceledException("Pipeline execution was canceled");

                await diagnosticsManager.CollectDockerExecFailureDiagnosticsAsync(
                    exception,
                    dockerPath,
                    dockerArgs,
                    containerId);
            }
        }
    }
}