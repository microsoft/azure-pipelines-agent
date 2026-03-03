// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class ContainerOperationProviderL0 : ContainerOperationProviderL0Base
    {

        [Fact(Skip = "The test is flaky and needs to be fixed using the new container strategy.")]
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
                
                var provider = new ContainerOperationProvider();
                provider.Initialize(hc);

                // Act - Call main container code with mocked Docker operations
                await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });

                // Assert
                Assert.Equal(NodePathFromLabel, container.CustomNodePath);
                Assert.Equal(NodePathFromLabel, container.ResultNodePath);
                Assert.Contains(NodePathFromLabel, container.ContainerCommand);
            }
        }

        [Fact(Skip = "The test is flaky and needs to be fixed using the new container strategy.")]
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
                
                var provider = new ContainerOperationProvider();
                provider.Initialize(hc);
                
                typeof(ContainerOperationProvider).GetField("_dockerManger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.SetValue(provider, dockerManager.Object);

                // Act - Call main container code with mocked Docker operations
                await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });

                // Assert - macOS uses "node" from container
                Assert.Equal(DefaultNodeCommand, container.CustomNodePath);
                Assert.Equal(DefaultNodeCommand, container.ResultNodePath);
                Assert.Contains(DefaultNodeCommand, container.ContainerCommand);
            }
        }

        // Test 3: Docker label absent - Windows + Linux container only
        [Fact(Skip = "The test is flaky and needs to be fixed using the new container strategy.")]
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
                
                var provider = new ContainerOperationProvider();
                provider.Initialize(hc);
                
                typeof(ContainerOperationProvider).GetField("_dockerManger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.SetValue(provider, dockerManager.Object);

                // Act - Call main container code with mocked Docker operations
                await provider.StartContainersAsync(executionContext.Object, new List<ContainerInfo> { container });

                // Assert - Windows+Linux uses "node" from container
                Assert.Equal(DefaultNodeCommand, container.CustomNodePath);
                Assert.Equal(DefaultNodeCommand, container.ResultNodePath);
                Assert.Contains(DefaultNodeCommand, container.ContainerCommand);
            }
        }

        // Test 4: Docker label absent - Linux only (uses agent's mounted node from externals)
        [Fact(Skip = "The test is flaky and needs to be fixed using the new container strategy.")]
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
                
                var provider = new ContainerOperationProvider();
                provider.Initialize(hc);
                
                typeof(ContainerOperationProvider).GetField("_dockerManger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.SetValue(provider, dockerManager.Object);

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
    }
}
