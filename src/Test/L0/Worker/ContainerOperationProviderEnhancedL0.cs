// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class ContainerOperationProviderEnhancedL0 : ContainerOperationProviderL0Base
    {
        [Theory]
        [InlineData(false, true, true)]
        [InlineData(false, false, true)]
        [InlineData(false, false, false)]
        [InlineData(true, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, false, false)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task StartContainer_DefaultsToNode24ThenNode20(
            bool useStrategy, bool node24Works, bool node20Works)
        {
            if (PlatformUtil.RunningOnMacOS)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                var dockerManager = CreateDockerManagerMock(NodePathFromLabelEmpty);
                string startupLabel = node24Works ? "container-startup-using-node-24" :
                    node20Works ? "container-startup-using-node-20" : "container-startup-failed";
                dockerManager.Setup(x => x.GetDockerLogs(It.IsAny<IExecutionContext>(), It.IsAny<string>()))
                    .ReturnsAsync(new List<string> { startupLabel });
                var probedVersions = new List<int>();
                dockerManager.Setup(x => x.DockerExec(
                    It.IsAny<IExecutionContext>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.Is<string>(command => command.Contains("--version")), It.IsAny<List<string>>()))
                    .ReturnsAsync((IExecutionContext _, string _, string _, string command, List<string> output) =>
                    {
                        int version = command.Contains("node24") ? 24 : 20;
                        probedVersions.Add(version);
                        bool works = version == 24 ? node24Works : node20Works;
                        if (works)
                        {
                            output.Add($"v{version}.0.0");
                        }
                        return works ? 0 : 127;
                    });
                var executionContext = CreateExecutionContextMock(hc, useNodeVersionStrategy: useStrategy);
                executionContext.Setup(x => x.GetHostContext()).Returns(hc);
                var container = new ContainerInfo(new Pipelines.ContainerResource { Alias = "test", Image = "test-image" })
                {
                    ImageOS = PlatformUtil.HostOS
                };
                if (!PlatformUtil.RunningOnWindows)
                {
                    SetupProcessInvokerMock(hc);
                }
                hc.SetSingleton<IDockerCommandManager>(dockerManager.Object);
                var provider = new ContainerOperationProviderEnhanced();
                provider.Initialize(hc);

                if (!node24Works && !node20Works)
                {
                    await Assert.ThrowsAsync<System.NotSupportedException>(() =>
                        provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container }));
                }
                else
                {
                    await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });
                    Assert.Contains(node24Works ? "node24" : "node20_1", container.ResultNodePath);
                }

                if (useStrategy)
                {
                    Assert.Equal(node24Works ? new[] { 24 } : new[] { 24, 20 }, probedVersions);
                }
                else
                {
                    Assert.True(container.ContainerCommand.IndexOf("node24", System.StringComparison.Ordinal) <
                        container.ContainerCommand.IndexOf("node20_1", System.StringComparison.Ordinal));
                }
            }
        }

        // =============================================
        // Legacy path tests (UseEnhancedNodeSelection = false)
        // =============================================

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task StartContainer_Legacy_FallsBackToManuallyInstalledNode16(bool node16Installed, bool eolPolicyEnabled)
        {
            bool expectNode16 = node16Installed && !eolPolicyEnabled;
            if (PlatformUtil.RunningOnMacOS)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                string node16Folder = System.IO.Path.Combine(hc.GetDirectory(WellKnownDirectory.Externals), "node16");
                if (node16Installed)
                {
                    string node16Bin = System.IO.Path.Combine(node16Folder, "bin");
                    System.IO.Directory.CreateDirectory(node16Bin);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(node16Bin, $"node{IOUtil.ExeExtension}"), string.Empty);
                }

                try
                {
                    var dockerManager = CreateDockerManagerMock(NodePathFromLabelEmpty);
                    dockerManager.Setup(x => x.GetDockerLogs(It.IsAny<IExecutionContext>(), It.IsAny<string>()))
                        .ReturnsAsync(new List<string> { expectNode16 ? "container-startup-using-node-16" : "container-startup-failed" });
                    var executionContext = CreateExecutionContextMock(hc);
                    executionContext.Setup(x => x.GetVariableValueOrDefault("AGENT_RESTRICT_EOL_NODE_VERSIONS"))
                        .Returns(eolPolicyEnabled ? "true" : "false");
                    var container = new ContainerInfo(new Pipelines.ContainerResource { Alias = "test", Image = "test-image" })
                    {
                        ImageOS = PlatformUtil.HostOS
                    };
                    if (!PlatformUtil.RunningOnWindows)
                    {
                        SetupProcessInvokerMock(hc);
                    }
                    hc.SetSingleton<IDockerCommandManager>(dockerManager.Object);
                    var provider = new ContainerOperationProviderEnhanced();
                    provider.Initialize(hc);

                    if (expectNode16)
                    {
                        await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });
                        Assert.Contains("node16", container.ResultNodePath);
                        Assert.True(container.ContainerCommand.IndexOf("node20_1", System.StringComparison.Ordinal) <
                            container.ContainerCommand.IndexOf("node16", System.StringComparison.Ordinal));
                        executionContext.Verify(x => x.AddIssue(
                            It.Is<Issue>(issue => issue.Type == IssueType.Warning && issue.Message.Contains("Node 16"))), Times.Once);
                    }
                    else
                    {
                        await Assert.ThrowsAsync<System.NotSupportedException>(() =>
                            provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container }));
                        Assert.DoesNotContain("node16", container.ContainerCommand);
                    }
                }
                finally
                {
                    if (node16Installed)
                    {
                        System.IO.Directory.Delete(node16Folder, recursive: true);
                    }
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task StartContainer_WithDockerLabel_SetsNodePath()
        {
            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                
                var dockerManager = CreateDockerManagerMock(NodePathFromLabel);
                var executionContext = CreateExecutionContextMock(hc);
                var container = new ContainerInfo(new Pipelines.ContainerResource() { Alias = "test", Image = "node:16" });

                // Setup IProcessInvoker for non-Windows platforms
                if (!PlatformUtil.RunningOnWindows)
                {
                    SetupProcessInvokerMock(hc);
                }

                hc.SetSingleton<IDockerCommandManager>(dockerManager.Object);
                
                var provider = new ContainerOperationProviderEnhanced();
                provider.Initialize(hc);

                // Act - Call main container code with mocked Docker operations
                await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });

                // Assert
                Assert.Equal(NodePathFromLabel, container.CustomNodePath);
                Assert.Equal(NodePathFromLabel, container.ResultNodePath);
                Assert.Contains(NodePathFromLabel, container.ContainerCommand);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "windows")]
        [Trait("SkipOn", "linux")]
        public async Task StartContainer_WithoutDockerLabel_OnMacOS_UsesDefaultNode()
        {
            // Only run on macOS
            if (!PlatformUtil.RunningOnMacOS)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                
                var dockerManager = CreateDockerManagerMock(NodePathFromLabelEmpty);
                var executionContext = CreateExecutionContextMock(hc);
                var container = new ContainerInfo(new Pipelines.ContainerResource() { Alias = "test", Image = "node:16" });

                // Setup IProcessInvoker for macOS
                SetupProcessInvokerMock(hc);

                hc.SetSingleton<IDockerCommandManager>(dockerManager.Object);
                
                var provider = new ContainerOperationProviderEnhanced();
                provider.Initialize(hc);

                // Act - Call main container code with mocked Docker operations
                await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });

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
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public async Task StartContainer_WithoutDockerLabel_OnWindowsWithLinuxContainer_UsesDefaultNode()
        {
            // Only run on Windows
            if (!PlatformUtil.RunningOnWindows)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                
                var dockerManager = CreateDockerManagerMock(NodePathFromLabelEmpty);
                var executionContext = CreateExecutionContextMock(hc);
                var container = new ContainerInfo(new Pipelines.ContainerResource() { Alias = "test", Image = "node:16" });
                // Set container to Linux OS (Windows host running Linux container)
                container.ImageOS = PlatformUtil.OS.Linux;

                hc.SetSingleton<IDockerCommandManager>(dockerManager.Object);
                
                var provider = new ContainerOperationProviderEnhanced();
                provider.Initialize(hc);

                // Act - Call main container code with mocked Docker operations
                await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });

                // Assert - Windows+Linux uses "node" from container
                Assert.Equal(DefaultNodeCommand, container.CustomNodePath);
                Assert.Equal(DefaultNodeCommand, container.ResultNodePath);
                Assert.Contains(DefaultNodeCommand, container.ContainerCommand);
            }
        }

        // Test 4: Docker label absent - Linux only (uses agent's mounted node from externals)
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "windows")]
        public async Task StartContainer_WithoutDockerLabel_OnLinux_UsesAgentNode()
        {
            // Only run on Linux
            if (!PlatformUtil.RunningOnLinux)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                
                var dockerManager = CreateDockerManagerMock(NodePathFromLabelEmpty);
                var executionContext = CreateExecutionContextMock(hc);
                var container = new ContainerInfo(new Pipelines.ContainerResource() { Alias = "test", Image = "node:16" });

                // Setup IProcessInvoker for Linux
                SetupProcessInvokerMock(hc);

                hc.SetSingleton<IDockerCommandManager>(dockerManager.Object);
                
                var provider = new ContainerOperationProviderEnhanced();
                provider.Initialize(hc);

                // Act - Call main container code with mocked Docker operations
                await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });

                // Assert - Linux uses agent's mounted node
                Assert.True(string.IsNullOrEmpty(container.CustomNodePath));
                Assert.NotNull(container.ResultNodePath);
                Assert.NotEmpty(container.ResultNodePath);
                Assert.Contains(NodeFromAgentExternal, container.ResultNodePath);
                Assert.EndsWith("/bin/node", container.ResultNodePath);
                Assert.Contains(NodeFromAgentExternal, container.ContainerCommand);
            }
        }

        // =============================================
        // UseEnhancedNodeSelection path tests
        // Knob activated via Moq-specific matcher overrides
        // in CreateExecutionContextMock(hc, useNodeVersionStrategy: true).
        // No env vars used — avoids parallel test pollution.
        // =============================================

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task StartContainer_UseNodeVersionStrategy_WithDockerLabel_UsesSleepOrPing()
        {
            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                
                var dockerManager = CreateDockerManagerMock(NodePathFromLabel);
                var executionContext = CreateExecutionContextMock(hc, useNodeVersionStrategy: true);
                var container = new ContainerInfo(new Pipelines.ContainerResource() { Alias = "test", Image = "node:16" });

                if (!PlatformUtil.RunningOnWindows)
                {
                    SetupProcessInvokerMock(hc);
                }

                hc.SetSingleton<IDockerCommandManager>(dockerManager.Object);
                
                var provider = new ContainerOperationProviderEnhanced();
                provider.Initialize(hc);

                await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });

                Assert.Equal(NodePathFromLabel, container.CustomNodePath);
                if (PlatformUtil.RunningOnWindows)
                {
                    Assert.Contains("ping -t localhost", container.ContainerCommand);
                }
                else
                {
                    Assert.Equal("bash -c \"sleep infinity\"", container.ContainerCommand);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public async Task StartContainer_UseNodeVersionStrategy_OnWindows_LinuxContainer_UsesSleepInfinity()
        {
            if (!PlatformUtil.RunningOnWindows)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                
                var dockerManager = CreateDockerManagerMock(NodePathFromLabelEmpty);
                var executionContext = CreateExecutionContextMock(hc, useNodeVersionStrategy: true);
                var container = new ContainerInfo(new Pipelines.ContainerResource() { Alias = "test", Image = "ubuntu:22.04" });
                container.ImageOS = PlatformUtil.OS.Linux;

                hc.SetSingleton<IDockerCommandManager>(dockerManager.Object);
                
                var provider = new ContainerOperationProviderEnhanced();
                provider.Initialize(hc);

                await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });

                Assert.Equal("bash -c \"sleep infinity\"", container.ContainerCommand);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public async Task StartContainer_UseNodeVersionStrategy_OnWindows_WindowsContainer_UsesPing()
        {
            if (!PlatformUtil.RunningOnWindows)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                
                var dockerManager = CreateDockerManagerMock(NodePathFromLabelEmpty);
                var executionContext = CreateExecutionContextMock(hc, useNodeVersionStrategy: true);
                var container = new ContainerInfo(new Pipelines.ContainerResource() { Alias = "test", Image = "myteam-custom-builder:latest" });
                container.ImageOS = PlatformUtil.OS.Windows;

                hc.SetSingleton<IDockerCommandManager>(dockerManager.Object);
                
                var provider = new ContainerOperationProviderEnhanced();
                provider.Initialize(hc);

                await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });

                Assert.Contains("ping -t localhost", container.ContainerCommand);
                Assert.Contains("nul", container.ContainerCommand);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "windows")]
        public async Task StartContainer_UseNodeVersionStrategy_OnLinux_UsesSleepInfinity()
        {
            if (!PlatformUtil.RunningOnLinux)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                
                var dockerManager = CreateDockerManagerMock(NodePathFromLabelEmpty);
                var executionContext = CreateExecutionContextMock(hc, useNodeVersionStrategy: true);
                var container = new ContainerInfo(new Pipelines.ContainerResource() { Alias = "test", Image = "ubuntu:22.04" });

                SetupProcessInvokerMock(hc);

                hc.SetSingleton<IDockerCommandManager>(dockerManager.Object);
                
                var provider = new ContainerOperationProviderEnhanced();
                provider.Initialize(hc);

                await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });

                Assert.Equal("bash -c \"sleep infinity\"", container.ContainerCommand);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "windows")]
        public async Task StartContainer_WhenDockerSocketIsOmittedOrFalse_DoesNotProbeSocket(bool explicitlyDisableSocket)
        {
            if (PlatformUtil.RunningOnWindows)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));

                var dockerManager = CreateDockerManagerMock(NodePathFromLabel);
                var executionContext = CreateExecutionContextMock(hc);
                var dockerContainer = new Pipelines.ContainerResource() { Alias = "test", Image = "node:16" };
                if (explicitlyDisableSocket)
                {
                    dockerContainer.Properties.Set<bool>("mapDockerSocket", false);
                }
                var container = new ContainerInfo(dockerContainer);
                var processInvoker = SetupProcessInvokerMockForVerification(hc);

                hc.SetSingleton<IDockerCommandManager>(dockerManager.Object);

                var provider = new ContainerOperationProviderEnhanced();
                provider.Initialize(hc);

                await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });

                processInvoker.Verify(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    "stat",
                    It.Is<string>(arguments => arguments.Contains("/var/run/docker.sock")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<CancellationToken>()),
                    Times.Never);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "windows")]
        public async Task StartContainer_WhenDockerSocketIsMapped_ProbesSocketForDockerGroupSetup()
        {
            if (PlatformUtil.RunningOnWindows)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));

                var dockerManager = CreateDockerManagerMock(NodePathFromLabel);
                var executionContext = CreateExecutionContextMock(hc);
                executionContext.Setup(x => x.GetVariableValueOrDefault("VSTS_SETUP_DOCKERGROUP")).Returns("true");
                var dockerContainer = new Pipelines.ContainerResource() { Alias = "test", Image = "node:16" };
                dockerContainer.Properties.Set<bool>("mapDockerSocket", true);
                var container = new ContainerInfo(dockerContainer);
                var processInvoker = SetupProcessInvokerMockForVerification(hc);

                hc.SetSingleton<IDockerCommandManager>(dockerManager.Object);

                var provider = new ContainerOperationProviderEnhanced();
                provider.Initialize(hc);

                await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });

                processInvoker.Verify(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    "stat",
                    It.Is<string>(arguments => arguments.Contains("/var/run/docker.sock")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Text.Encoding>(),
                    It.IsAny<CancellationToken>()),
                    Times.Once);
            }
        }
    }
}
