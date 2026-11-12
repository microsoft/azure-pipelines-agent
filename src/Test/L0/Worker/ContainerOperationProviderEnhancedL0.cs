// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies;
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
        // =============================================
        // Legacy path tests (UseEnhancedNodeSelection = false)
        // =============================================

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task StartContainer_WithoutNode20_UsesNode24OrFails(bool enhanced, bool node24Fails)
        {
            if (PlatformUtil.RunningOnMacOS)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                var nodeHelper = new Mock<INodeHandlerHelper>();
                nodeHelper.Setup(x => x.IsNodeFolderExist("node20_1", hc)).Returns(false);
                var docker = CreateDockerManagerMock(NodePathFromLabelEmpty);
                docker.Setup(x => x.DockerExec(It.IsAny<IExecutionContext>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>()))
                    .ReturnsAsync((IExecutionContext _, string _, string _, string command, List<string> output) =>
                    {
                        if (command.Contains("--version"))
                        {
                            Assert.Contains("node24", command);
                            output.Add("v24.0.0");
                            return node24Fails ? 127 : 0;
                        }
                        return 0;
                    });
                docker.Setup(x => x.GetDockerLogs(It.IsAny<IExecutionContext>(), It.IsAny<string>()))
                    .ReturnsAsync(new List<string> { node24Fails ? "container-startup-failed" : "container-startup-using-node-24" });
                var executionContext = CreateExecutionContextMock(hc, enhanced);
                executionContext.Setup(x => x.GetHostContext()).Returns(hc);
                var container = new ContainerInfo(new Pipelines.ContainerResource { Alias = "test", Image = "test-image" })
                {
                    ImageOS = PlatformUtil.HostOS
                };
                if (!PlatformUtil.RunningOnWindows)
                {
                    SetupProcessInvokerMock(hc);
                }
                hc.SetSingleton<IDockerCommandManager>(docker.Object);
                hc.SetSingleton<IGlibcCompatibilityInfoProvider>(new Mock<IGlibcCompatibilityInfoProvider>().Object);
                var provider = new ContainerOperationProviderEnhanced(nodeHelper.Object);
                provider.Initialize(hc);

                if (node24Fails)
                {
                    var exception = await Assert.ThrowsAsync<System.NotSupportedException>(() =>
                        provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container }));
                    Assert.Contains(enhanced ? "Container Node.js runtime selection failed" : "Container startup failed", exception.Message);
                    executionContext.Verify(x => x.AddIssue(It.Is<Issue>(i => i.Type == IssueType.Error)), Times.Never);
                    return;
                }

                await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });
                Assert.Contains("node24", container.ResultNodePath);
                if (!enhanced)
                {
                    Assert.Contains("node24", container.ContainerCommand);
                    Assert.DoesNotContain("node20_1", container.ContainerCommand);
                }
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task StartContainer_WithEolPolicy_DoesNotFallBackToNode20(bool node24Fails)
        {
            if (PlatformUtil.RunningOnMacOS)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                var nodeHelper = new Mock<INodeHandlerHelper>();
                nodeHelper.Setup(x => x.IsNodeFolderExist("node20_1", hc)).Returns(true);
                var docker = CreateDockerManagerMock(NodePathFromLabelEmpty);
                docker.Setup(x => x.GetDockerLogs(It.IsAny<IExecutionContext>(), It.IsAny<string>()))
                    .ReturnsAsync(new List<string> { node24Fails ? "container-startup-failed" : "container-startup-using-node-24" });
                var executionContext = CreateExecutionContextMock(hc);
                executionContext.Setup(x => x.GetVariableValueOrDefault("AGENT_RESTRICT_EOL_NODE_VERSIONS")).Returns("true");
                var container = new ContainerInfo(new Pipelines.ContainerResource { Alias = "test", Image = "test-image" })
                {
                    ImageOS = PlatformUtil.HostOS
                };
                if (!PlatformUtil.RunningOnWindows)
                {
                    SetupProcessInvokerMock(hc);
                }
                hc.SetSingleton<IDockerCommandManager>(docker.Object);
                var provider = new ContainerOperationProviderEnhanced(nodeHelper.Object);
                provider.Initialize(hc);

                if (node24Fails)
                {
                    var exception = await Assert.ThrowsAsync<System.NotSupportedException>(() =>
                        provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container }));
                    Assert.Contains("Node 24", exception.Message);
                    Assert.DoesNotContain("Node 20", exception.Message);
                    executionContext.Verify(x => x.AddIssue(It.Is<Issue>(i => i.Type == IssueType.Error)), Times.Never);
                }
                else
                {
                    await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });
                    Assert.Contains("node24", container.ResultNodePath);
                }

                Assert.Contains("node24", container.ContainerCommand);
                Assert.DoesNotContain("node20_1", container.ContainerCommand);
            }
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, false, false)]
        [InlineData(false, true, true)]
        [InlineData(true, true, true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task StartContainer_Node24OnlyAndContainerStopped_Fails(bool enhanced, bool restrictEol, bool node20Available)
        {
            if (PlatformUtil.RunningOnMacOS)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                var nodeHelper = new Mock<INodeHandlerHelper>();
                nodeHelper.Setup(x => x.IsNodeFolderExist("node20_1", hc)).Returns(node20Available);
                var docker = CreateDockerManagerMock(NodePathFromLabelEmpty);
                docker.Setup(x => x.DockerPS(It.IsAny<IExecutionContext>(), It.Is<string>(options => options.Contains("--filter status=running"))))
                    .ReturnsAsync(new List<string>());
                var executionContext = CreateExecutionContextMock(hc, enhanced);
                executionContext.Setup(x => x.GetVariableValueOrDefault("AGENT_RESTRICT_EOL_NODE_VERSIONS"))
                    .Returns(restrictEol ? "true" : "false");
                var container = new ContainerInfo(new Pipelines.ContainerResource { Alias = "test", Image = "test-image" })
                {
                    ImageOS = PlatformUtil.HostOS
                };
                if (!PlatformUtil.RunningOnWindows)
                {
                    SetupProcessInvokerMock(hc);
                }
                hc.SetSingleton<IDockerCommandManager>(docker.Object);
                var provider = new ContainerOperationProviderEnhanced(nodeHelper.Object);
                provider.Initialize(hc);

                var exception = await Assert.ThrowsAsync<System.NotSupportedException>(() =>
                    provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container }));

                Assert.Contains("Node 24", exception.Message);
                Assert.Contains("not running", exception.Message);
                executionContext.Verify(x => x.AddIssue(It.Is<Issue>(i => i.Type == IssueType.Error)), Times.Never);
            }
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, false, false)]
        [InlineData(false, true, true)]
        [InlineData(true, true, true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task StartContainer_Node24OnlyCannotVerifyRunningState_Fails(bool enhanced, bool restrictEol, bool node20Available)
        {
            if (PlatformUtil.RunningOnMacOS)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                var nodeHelper = new Mock<INodeHandlerHelper>();
                nodeHelper.Setup(x => x.IsNodeFolderExist("node20_1", hc)).Returns(node20Available);
                var docker = CreateDockerManagerMock(NodePathFromLabelEmpty);
                docker.Setup(x => x.DockerPS(It.IsAny<IExecutionContext>(), It.Is<string>(options => options.Contains("--filter status=running"))))
                    .ThrowsAsync(new System.InvalidOperationException("Docker status unavailable"));
                var executionContext = CreateExecutionContextMock(hc, enhanced);
                executionContext.Setup(x => x.GetVariableValueOrDefault("AGENT_RESTRICT_EOL_NODE_VERSIONS"))
                    .Returns(restrictEol ? "true" : "false");
                var container = new ContainerInfo(new Pipelines.ContainerResource { Alias = "test", Image = "test-image" })
                {
                    ImageOS = PlatformUtil.HostOS
                };
                if (!PlatformUtil.RunningOnWindows)
                {
                    SetupProcessInvokerMock(hc);
                }
                hc.SetSingleton<IDockerCommandManager>(docker.Object);
                var provider = new ContainerOperationProviderEnhanced(nodeHelper.Object);
                provider.Initialize(hc);

                var exception = await Assert.ThrowsAsync<System.NotSupportedException>(() =>
                    provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container }));

                Assert.Contains("Node 24", exception.Message);
                Assert.Contains("Docker status unavailable", exception.InnerException.Message);
            }
        }

        [Theory]
        [InlineData(false, false, false, false)]
        [InlineData(true, true, false, false)]
        [InlineData(false, false, false, true)]
        [InlineData(true, true, false, true)]
        [InlineData(true, true, true, false)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task StartContainer_Node24OnlyCannotReadStartupLogs_ProbesNode24(bool restrictEol, bool node20Available, bool cancelled, bool node24Runs)
        {
            if (PlatformUtil.RunningOnMacOS)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                var nodeHelper = new Mock<INodeHandlerHelper>();
                nodeHelper.Setup(x => x.IsNodeFolderExist("node20_1", hc)).Returns(node20Available);
                var docker = CreateDockerManagerMock(NodePathFromLabelEmpty);
                System.Exception logException = cancelled
                    ? new System.OperationCanceledException("Docker logs unavailable")
                    : new System.InvalidOperationException("Docker logs unavailable");
                docker.Setup(x => x.GetDockerLogs(It.IsAny<IExecutionContext>(), It.IsAny<string>()))
                    .ThrowsAsync(logException);
                docker.Setup(x => x.DockerExec(It.IsAny<IExecutionContext>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.Is<string>(command => command.Contains("--version")), It.IsAny<List<string>>()))
                    .ReturnsAsync((IExecutionContext _, string _, string _, string command, List<string> output) =>
                    {
                        Assert.Contains("node24", command);
                        if (node24Runs)
                        {
                            output.Add("v24.0.0");
                        }
                        return node24Runs ? 0 : 127;
                    });
                var executionContext = CreateExecutionContextMock(hc);
                executionContext.Setup(x => x.GetVariableValueOrDefault("AGENT_RESTRICT_EOL_NODE_VERSIONS"))
                    .Returns(restrictEol ? "true" : "false");
                var container = new ContainerInfo(new Pipelines.ContainerResource { Alias = "test", Image = "test-image" })
                {
                    ImageOS = PlatformUtil.HostOS
                };
                if (!PlatformUtil.RunningOnWindows)
                {
                    SetupProcessInvokerMock(hc);
                }
                hc.SetSingleton<IDockerCommandManager>(docker.Object);
                var provider = new ContainerOperationProviderEnhanced(nodeHelper.Object);
                provider.Initialize(hc);

                if (cancelled)
                {
                    var exception = await Assert.ThrowsAsync<System.OperationCanceledException>(() =>
                        provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container }));
                    Assert.Contains("Docker logs unavailable", exception.Message);
                }
                else if (node24Runs)
                {
                    await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });
                    Assert.Contains("node24", container.ResultNodePath);
                    executionContext.Verify(x => x.AddIssue(It.Is<Issue>(issue =>
                        issue.Type == IssueType.Warning && issue.Message.Contains("verified that Node 24"))), Times.Once);
                }
                else
                {
                    var exception = await Assert.ThrowsAsync<System.NotSupportedException>(() =>
                        provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container }));
                    Assert.Contains("Node 24", exception.Message);
                    Assert.Contains("Docker logs unavailable", exception.InnerException.Message);
                }
                Assert.DoesNotContain("node20_1", container.ResultNodePath);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task StartContainer_Node24OnlyMissingStartupMarker_ProbesNode24(bool node24Runs)
        {
            if (PlatformUtil.RunningOnMacOS)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                var nodeHelper = new Mock<INodeHandlerHelper>();
                nodeHelper.Setup(x => x.IsNodeFolderExist("node20_1", hc)).Returns(true);
                var docker = CreateDockerManagerMock(NodePathFromLabelEmpty);
                docker.Setup(x => x.GetDockerLogs(It.IsAny<IExecutionContext>(), It.IsAny<string>()))
                    .ReturnsAsync(new List<string>());
                docker.Setup(x => x.DockerExec(It.IsAny<IExecutionContext>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.Is<string>(command => command.Contains("--version")), It.IsAny<List<string>>()))
                    .ReturnsAsync((IExecutionContext _, string _, string _, string command, List<string> output) =>
                    {
                        Assert.Contains("node24", command);
                        if (node24Runs)
                        {
                            output.Add("v24.0.0");
                        }
                        return node24Runs ? 0 : 127;
                    });
                var executionContext = CreateExecutionContextMock(hc);
                executionContext.Setup(x => x.GetVariableValueOrDefault("AGENT_RESTRICT_EOL_NODE_VERSIONS")).Returns("true");
                var container = new ContainerInfo(new Pipelines.ContainerResource { Alias = "test", Image = "test-image" })
                {
                    ImageOS = PlatformUtil.HostOS
                };
                if (!PlatformUtil.RunningOnWindows)
                {
                    SetupProcessInvokerMock(hc);
                }
                hc.SetSingleton<IDockerCommandManager>(docker.Object);
                var provider = new ContainerOperationProviderEnhanced(nodeHelper.Object);
                provider.Initialize(hc);

                if (node24Runs)
                {
                    await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });
                    Assert.Contains("node24", container.ResultNodePath);
                    executionContext.Verify(x => x.AddIssue(It.Is<Issue>(issue =>
                        issue.Type == IssueType.Warning && issue.Message.Contains("verified that Node 24"))), Times.Once);
                }
                else
                {
                    var exception = await Assert.ThrowsAsync<System.NotSupportedException>(() =>
                        provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container }));
                    Assert.Contains("Node 24", exception.Message);
                }
                Assert.DoesNotContain("node20_1", container.ResultNodePath);
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task StartContainer_Node24OnlyRejectsNode20StartupMarker(bool restrictEol, bool node20Available)
        {
            if (PlatformUtil.RunningOnMacOS)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                var nodeHelper = new Mock<INodeHandlerHelper>();
                nodeHelper.Setup(x => x.IsNodeFolderExist("node20_1", hc)).Returns(node20Available);
                var docker = CreateDockerManagerMock(NodePathFromLabelEmpty);
                docker.Setup(x => x.GetDockerLogs(It.IsAny<IExecutionContext>(), It.IsAny<string>()))
                    .ReturnsAsync(new List<string> { "container-startup-using-node-20" });
                var executionContext = CreateExecutionContextMock(hc);
                executionContext.Setup(x => x.GetVariableValueOrDefault("AGENT_RESTRICT_EOL_NODE_VERSIONS"))
                    .Returns(restrictEol ? "true" : "false");
                var container = new ContainerInfo(new Pipelines.ContainerResource { Alias = "test", Image = "test-image" })
                {
                    ImageOS = PlatformUtil.HostOS
                };
                if (!PlatformUtil.RunningOnWindows)
                {
                    SetupProcessInvokerMock(hc);
                }
                hc.SetSingleton<IDockerCommandManager>(docker.Object);
                var provider = new ContainerOperationProviderEnhanced(nodeHelper.Object);
                provider.Initialize(hc);

                var exception = await Assert.ThrowsAsync<System.NotSupportedException>(() =>
                    provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container }));

                Assert.Contains("Node 20", exception.Message);
                Assert.Contains("Node 24", exception.Message);
                Assert.DoesNotContain("node20_1", container.ContainerCommand);
                executionContext.Verify(x => x.AddIssue(It.Is<Issue>(i => i.Type == IssueType.Error)), Times.Never);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task StartContainer_WithDockerLabelAndEolPolicy_PreservesCustomNodePath(bool enhanced)
        {
            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                var nodeHelper = new Mock<INodeHandlerHelper>();
                nodeHelper.Setup(x => x.IsNodeFolderExist("node20_1", hc)).Returns(false);
                var docker = CreateDockerManagerMock(NodePathFromLabel);
                var executionContext = CreateExecutionContextMock(hc, enhanced);
                executionContext.Setup(x => x.GetVariableValueOrDefault("AGENT_RESTRICT_EOL_NODE_VERSIONS")).Returns("true");
                var container = new ContainerInfo(new Pipelines.ContainerResource { Alias = "test", Image = "node:16" });
                if (!PlatformUtil.RunningOnWindows)
                {
                    SetupProcessInvokerMock(hc);
                }
                hc.SetSingleton<IDockerCommandManager>(docker.Object);
                var provider = new ContainerOperationProviderEnhanced(nodeHelper.Object);
                provider.Initialize(hc);

                await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });

                Assert.Equal(NodePathFromLabel, container.CustomNodePath);
                Assert.Equal(NodePathFromLabel, container.ResultNodePath);
                Assert.DoesNotContain("node24", container.ContainerCommand);
                Assert.DoesNotContain("node20_1", container.ContainerCommand);
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task StartContainer_CrossPlatformWithEolPolicy_OnlyUsesExplicitCustomNode(bool enhanced, bool hasCustomNode)
        {
            if (!PlatformUtil.RunningOnWindows && !PlatformUtil.RunningOnMacOS)
            {
                return;
            }

            using (var hc = new TestHostContext(this))
            {
                System.IO.Directory.CreateDirectory(hc.GetDirectory(WellKnownDirectory.Work));
                var docker = CreateDockerManagerMock(hasCustomNode ? NodePathFromLabel : NodePathFromLabelEmpty);
                var executionContext = CreateExecutionContextMock(hc, enhanced);
                executionContext.Setup(x => x.GetVariableValueOrDefault("AGENT_RESTRICT_EOL_NODE_VERSIONS")).Returns("true");
                var container = new ContainerInfo(new Pipelines.ContainerResource { Alias = "test", Image = "node:16" })
                {
                    ImageOS = PlatformUtil.OS.Linux
                };
                if (!PlatformUtil.RunningOnWindows)
                {
                    SetupProcessInvokerMock(hc);
                }
                hc.SetSingleton<IDockerCommandManager>(docker.Object);
                var provider = new ContainerOperationProviderEnhanced();
                provider.Initialize(hc);

                if (!hasCustomNode)
                {
                    var exception = await Assert.ThrowsAsync<System.NotSupportedException>(() =>
                        provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container }));
                    Assert.Contains("Node24", exception.Message);
                    Assert.Contains("com.azure.dev.pipelines.agent.handler.node.path", exception.Message);
                    return;
                }

                await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });

                Assert.Equal(NodePathFromLabel, container.CustomNodePath);
                Assert.Equal(NodePathFromLabel, container.ResultNodePath);
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
