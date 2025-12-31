// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Microsoft.VisualStudio.Services.Agent.Util;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class ContainerOperationProviderL0
    {
        // Test 1: Docker label present
        private const string NodePathFromLabel = "/usr/bin/node";

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task StartContainer_WithDockerLabel_SetsNodePath()
        {
            using (var hc = new TestHostContext(this))
            {
                // Arrange - Mock Docker to return node path
                var dockerManager = new Mock<IDockerCommandManager>();
                dockerManager.Setup(x => x.DockerInspect(It.IsAny<IExecutionContext>(), It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(NodePathFromLabel);

                var executionContext = new Mock<IExecutionContext>();
                executionContext.Setup(x => x.Variables).Returns(new Variables(hc, new Dictionary<string, VariableValue>()));
                
                var container = new ContainerInfo(hc, new Pipelines.ContainerResource() { Alias = "test", Image = "node:16" }, isJobContainer: true);

                var provider = new ContainerOperationProvider();
                provider.Initialize(hc);
                typeof(ContainerOperationProvider).GetField("_dockerManger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.SetValue(provider, dockerManager.Object);

                // Act
                await provider.StartContainerAsync(executionContext.Object, container);

                // Assert
                Assert.Equal(NodePathFromLabel, container.CustomNodePath);
                Assert.Equal(NodePathFromLabel, container.ResultNodePath);
                Assert.Contains(NodePathFromLabel, container.ContainerCommand);
            }
        }

        // Test 2: Docker label absent - macOS only
        private const string NodePathFromLabelEmpty = ""; // Label returns empty
        private const string DefaultNodeCommand = "node"; // macOS uses this

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "Windows")]
        [Trait("SkipOn", "Linux")]
        public async Task StartContainer_WithoutDockerLabel_OnMacOS_UsesDefaultNode()
        {
            // Only run on macOS
            if (!PlatformUtil.RunningOnMacOS)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                // Arrange - Mock Docker to return empty string (no label)
                var dockerManager = new Mock<IDockerCommandManager>();
                dockerManager.Setup(x => x.DockerInspect(It.IsAny<IExecutionContext>(), It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(NodePathFromLabelEmpty);

                var executionContext = new Mock<IExecutionContext>();
                executionContext.Setup(x => x.Variables).Returns(new Variables(hc, new Dictionary<string, VariableValue>()));
                
                var container = new ContainerInfo(hc, new Pipelines.ContainerResource() { Alias = "test", Image = "node:16" }, isJobContainer: true);

                var provider = new ContainerOperationProvider();
                provider.Initialize(hc);
                typeof(ContainerOperationProvider).GetField("_dockerManger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.SetValue(provider, dockerManager.Object);

                // Act
                await provider.StartContainerAsync(executionContext.Object, container);

                // Assert - macOS uses "node" from container
                Assert.Equal(DefaultNodeCommand, container.CustomNodePath);
                Assert.Equal(DefaultNodeCommand, container.ResultNodePath);
                Assert.Contains(DefaultNodeCommand, container.ContainerCommand);
            }
        }

        // Test 3: Docker label absent - Windows + Linux container only
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "macOS")]
        [Trait("SkipOn", "Linux")]
        public async Task StartContainer_WithoutDockerLabel_OnWindowsWithLinuxContainer_UsesDefaultNode()
        {
            // Only run on Windows
            if (!PlatformUtil.RunningOnWindows)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                // Arrange - Mock Docker to return empty string (no label)
                var dockerManager = new Mock<IDockerCommandManager>();
                dockerManager.Setup(x => x.DockerInspect(It.IsAny<IExecutionContext>(), It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(NodePathFromLabelEmpty);

                var executionContext = new Mock<IExecutionContext>();
                executionContext.Setup(x => x.Variables).Returns(new Variables(hc, new Dictionary<string, VariableValue>()));
                
                var container = new ContainerInfo(hc, new Pipelines.ContainerResource() { Alias = "test", Image = "node:16" }, isJobContainer: true);
                // Set container to Linux OS (Windows host running Linux container)
                container.ImageOS = PlatformUtil.OS.Linux;

                var provider = new ContainerOperationProvider();
                provider.Initialize(hc);
                typeof(ContainerOperationProvider).GetField("_dockerManger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.SetValue(provider, dockerManager.Object);

                // Act
                await provider.StartContainerAsync(executionContext.Object, container);

                // Assert - Windows+Linux uses "node" from container
                Assert.Equal(DefaultNodeCommand, container.CustomNodePath);
                Assert.Equal(DefaultNodeCommand, container.ResultNodePath);
                Assert.Contains(DefaultNodeCommand, container.ContainerCommand);
            }
        }

        // Test 4: Docker label absent - Linux only
        private const string NodeFromAgentExternal = "externals/node"; // Agent's node path contains this
        
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "macOS")]
        [Trait("SkipOn", "Windows")]
        public async Task StartContainer_WithoutDockerLabel_OnLinux_UsesAgentNode()
        {
            // Only run on Linux
            if (!PlatformUtil.RunningOnLinux)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                // Arrange - Mock Docker to return empty string (no label)
                var dockerManager = new Mock<IDockerCommandManager>();
                dockerManager.Setup(x => x.DockerInspect(It.IsAny<IExecutionContext>(), It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(NodePathFromLabelEmpty);

                var executionContext = new Mock<IExecutionContext>();
                executionContext.Setup(x => x.Variables).Returns(new Variables(hc, new Dictionary<string, VariableValue>()));
                
                var container = new ContainerInfo(hc, new Pipelines.ContainerResource() { Alias = "test", Image = "node:16" }, isJobContainer: true);

                var provider = new ContainerOperationProvider();
                provider.Initialize(hc);
                typeof(ContainerOperationProvider).GetField("_dockerManger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.SetValue(provider, dockerManager.Object);

                // Act
                await provider.StartContainerAsync(executionContext.Object, container);

                // Assert - Linux uses agent's mounted node
                Assert.True(string.IsNullOrEmpty(container.CustomNodePath));
                Assert.NotNull(container.ResultNodePath);
                Assert.NotEmpty(container.ResultNodePath);
                Assert.Contains(NodeFromAgentExternal, container.ResultNodePath);
                Assert.EndsWith("/bin/node", container.ResultNodePath);
                Assert.Contains(NodeFromAgentExternal, container.ContainerCommand);
            }
        }
    }
}
