// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Moq;
using Xunit;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class NodeHandlerL0
    {
        private Mock<INodeHandlerHelper> nodeHandlerHalper;

        public NodeHandlerL0()
        {
            nodeHandlerHalper = GetMockedNodeHandlerHelper();
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNodeForNodeHandlerEnvVarNotSet()
        {
            var agentUseNode10 = Environment.GetEnvironmentVariable("AGENT_USE_NODE10");
            Environment.SetEnvironmentVariable("AGENT_USE_NODE10", null);
            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                NodeHandler nodeHandler = new NodeHandler(nodeHandlerHalper.Object);

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc);
                nodeHandler.Data = new NodeHandlerData();

                string nodeVersion = "node"; // version 6
                if (PlatformUtil.RunningOnAlpine)
                {
                    nodeVersion = "node10"; // version 6 does not exist on Alpine
                }

                string actualLocation = nodeHandler.GetNodeLocation();
                string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                    nodeVersion,
                    "bin",
                    $"node{IOUtil.ExeExtension}");
                Assert.Equal(expectedLocation, actualLocation);
            }
            Environment.SetEnvironmentVariable("AGENT_USE_NODE10", agentUseNode10);
        }

        [Theory]
        [InlineData("node10")]
        [InlineData("node16")]
        [InlineData("node20")]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNewNodeForNewNodeHandler(string nodeVersion)
        {
            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                NodeHandler nodeHandler = new NodeHandler(nodeHandlerHalper.Object);

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc);
                nodeHandler.Data = nodeVersion switch
                {
                    "node10" => new Node10HandlerData(),
                    "node16" => new Node16HandlerData(),
                    "node20" => new Node20HandlerData(),
                    _ => throw new Exception("Invalid node version"),
                };

                string actualLocation = nodeHandler.GetNodeLocation();
                string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                    nodeVersion,
                    "bin",
                    $"node{IOUtil.ExeExtension}");
                Assert.Equal(expectedLocation, actualLocation);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNewNodeForNodeHandlerEnvVarSet()
        {
            try
            {
                Environment.SetEnvironmentVariable("AGENT_USE_NODE10", "true");

                using (TestHostContext thc = CreateTestHostContext())
                {
                    thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                    thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                    NodeHandler nodeHandler = new NodeHandler(nodeHandlerHalper.Object);

                    nodeHandler.Initialize(thc);
                    nodeHandler.ExecutionContext = CreateTestExecutionContext(thc);
                    nodeHandler.Data = new Node10HandlerData();

                    string actualLocation = nodeHandler.GetNodeLocation();
                    string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                        "node10",
                        "bin",
                        $"node{IOUtil.ExeExtension}");
                    Assert.Equal(expectedLocation, actualLocation);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("AGENT_USE_NODE10", null);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNewNodeForNodeHandlerHostContextVarSet()
        {
            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                var variables = new Dictionary<string, VariableValue>();

                variables.Add("AGENT_USE_NODE10", new VariableValue("true"));

                NodeHandler nodeHandler = new NodeHandler(nodeHandlerHalper.Object);

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc, variables);
                nodeHandler.Data = new Node10HandlerData();

                string actualLocation = nodeHandler.GetNodeLocation();
                string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                    "node10",
                    "bin",
                    $"node{IOUtil.ExeExtension}");
                Assert.Equal(expectedLocation, actualLocation);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNewNodeForNewNodeHandlerHostContextVarUnset()
        {
            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                var variables = new Dictionary<string, VariableValue>();

                // Explicitly set variable feature flag to false
                variables.Add("AGENT_USE_NODE10", new VariableValue("false"));

                NodeHandler nodeHandler = new NodeHandler(nodeHandlerHalper.Object);

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc, variables);
                nodeHandler.Data = new Node10HandlerData();

                string actualLocation = nodeHandler.GetNodeLocation();
                string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                    "node10",
                    "bin",
                    $"node{IOUtil.ExeExtension}");
                Assert.Equal(expectedLocation, actualLocation);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseLTSNodeIfUseNodeKnobIsLTS()
        {
            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                Mock<INodeHandlerHelper> mockedNodeHandlerHelper = GetMockedNodeHandlerHelper();
                mockedNodeHandlerHelper
                    .SetupSequence(x => x.IsNodeFolderExist(It.IsAny<string>(), It.IsAny<IHostContext>()))
                    .Returns(false)
                    .Returns(true);

                mockedNodeHandlerHelper
                    .Setup(x => x.GetFilteredPossibleNodeFolders(It.IsAny<string>(), It.IsAny<string[]>()))
                    .Returns(new string[] { "node16" });

                var variables = new Dictionary<string, VariableValue>();

                variables.Add("AGENT_USE_NODE", new VariableValue("lts"));

                NodeHandler nodeHandler = new NodeHandler(mockedNodeHandlerHelper.Object);

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc, variables);
                nodeHandler.Data = new Node10HandlerData();

                string actualLocation = nodeHandler.GetNodeLocation();
                string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                    "node16",
                    "bin",
                    $"node{IOUtil.ExeExtension}");
                Assert.Equal(expectedLocation, actualLocation);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ThrowExceptionIfUseNodeKnobIsLTSAndLTSNotAvailable()
        {
            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                Mock<INodeHandlerHelper> mockedNodeHandlerHelper = GetMockedNodeHandlerHelper();
                mockedNodeHandlerHelper
                    .SetupSequence(x => x.IsNodeFolderExist(It.IsAny<string>(), It.IsAny<IHostContext>()))
                    .Returns(false)
                    .Returns(false);

                mockedNodeHandlerHelper
                    .Setup(x => x.GetFilteredPossibleNodeFolders(It.IsAny<string>(), It.IsAny<string[]>()))
                    .Returns(new string[] { "node16" });

                var variables = new Dictionary<string, VariableValue>();

                variables.Add("AGENT_USE_NODE", new VariableValue("lts"));

                NodeHandler nodeHandler = new NodeHandler(mockedNodeHandlerHelper.Object);

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc, variables);
                nodeHandler.Data = new Node10HandlerData();

                Assert.Throws<FileNotFoundException>(() => nodeHandler.GetNodeLocation());
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ThrowExceptionIfUseNodeKnobIsLTSAndFilteredPossibleNodeFoldersEmpty()
        {
            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                Mock<INodeHandlerHelper> mockedNodeHandlerHelper = GetMockedNodeHandlerHelper();
                mockedNodeHandlerHelper
                    .Setup(x => x.IsNodeFolderExist(It.IsAny<string>(), It.IsAny<IHostContext>()))
                    .Returns(false);

                var variables = new Dictionary<string, VariableValue>();

                variables.Add("AGENT_USE_NODE", new VariableValue("lts"));

                NodeHandler nodeHandler = new NodeHandler(mockedNodeHandlerHelper.Object);

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc, variables);
                nodeHandler.Data = new Node10HandlerData();

                Assert.Throws<FileNotFoundException>(() => nodeHandler.GetNodeLocation());
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseFirstAvailableNodeIfUseNodeKnobIsUpgrade()
        {
            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                Mock<INodeHandlerHelper> mockedNodeHandlerHelper = GetMockedNodeHandlerHelper();
                mockedNodeHandlerHelper
                    .SetupSequence(x => x.IsNodeFolderExist(It.IsAny<string>(), It.IsAny<IHostContext>()))
                    .Returns(false)
                    .Returns(true);
                mockedNodeHandlerHelper
                .Setup(x => x.GetFilteredPossibleNodeFolders(It.IsAny<string>(), It.IsAny<string[]>()))
                .Returns(new string[] { "nextAvailableNode1", "nextAvailableNode2" });

                var variables = new Dictionary<string, VariableValue>();

                variables.Add("AGENT_USE_NODE", new VariableValue("upgrade"));

                NodeHandler nodeHandler = new NodeHandler(mockedNodeHandlerHelper.Object);

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc, variables);
                nodeHandler.Data = new Node10HandlerData();

                string actualLocation = nodeHandler.GetNodeLocation();
                string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                    "nextAvailableNode1",
                    "bin",
                    $"node{IOUtil.ExeExtension}");
                Assert.Equal(expectedLocation, actualLocation);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseSecondAvailableNodeIfUseNodeKnobIsUpgradeFilteredNodeFoldersFirstNotAvailable()
        {
            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                Mock<INodeHandlerHelper> mockedNodeHandlerHelper = GetMockedNodeHandlerHelper();
                mockedNodeHandlerHelper
                    .SetupSequence(x => x.IsNodeFolderExist(It.IsAny<string>(), It.IsAny<IHostContext>()))
                    .Returns(false)
                    .Returns(false)
                    .Returns(true);
                mockedNodeHandlerHelper
                .Setup(x => x.GetFilteredPossibleNodeFolders(It.IsAny<string>(), It.IsAny<string[]>()))
                .Returns(new string[] { "nextAvailableNode1", "nextAvailableNode2" });

                var variables = new Dictionary<string, VariableValue>();

                variables.Add("AGENT_USE_NODE", new VariableValue("upgrade"));

                NodeHandler nodeHandler = new NodeHandler(mockedNodeHandlerHelper.Object);

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc, variables);
                nodeHandler.Data = new Node10HandlerData();

                string actualLocation = nodeHandler.GetNodeLocation();
                string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                    "nextAvailableNode2",
                    "bin",
                    $"node{IOUtil.ExeExtension}");
                Assert.Equal(expectedLocation, actualLocation);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ThrowExceptionIfUseNodeKnobIsUpgradeFilteredNodeFoldersAllNotAvailable()
        {
            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                Mock<INodeHandlerHelper> mockedNodeHandlerHelper = GetMockedNodeHandlerHelper();
                mockedNodeHandlerHelper
                    .SetupSequence(x => x.IsNodeFolderExist(It.IsAny<string>(), It.IsAny<IHostContext>()))
                    .Returns(false)
                    .Returns(false)
                    .Returns(false);
                mockedNodeHandlerHelper
                .Setup(x => x.GetFilteredPossibleNodeFolders(It.IsAny<string>(), It.IsAny<string[]>()))
                .Returns(new string[] { "nextAvailableNode1", "nextAvailableNode2" });

                var variables = new Dictionary<string, VariableValue>();

                variables.Add("AGENT_USE_NODE", new VariableValue("upgrade"));

                NodeHandler nodeHandler = new NodeHandler(mockedNodeHandlerHelper.Object);

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc, variables);
                nodeHandler.Data = new Node10HandlerData();

                Assert.Throws<FileNotFoundException>(() => nodeHandler.GetNodeLocation());
            }
        }

        private TestHostContext CreateTestHostContext([CallerMemberName] string testName = "")
        {
            return new TestHostContext(this, testName);
        }

        private IExecutionContext CreateTestExecutionContext(TestHostContext tc,
            Dictionary<string, VariableValue> variables = null)
        {
            var trace = tc.GetTrace();
            var executionContext = new Mock<IExecutionContext>();
            List<string> warnings;
            variables = variables ?? new Dictionary<string, VariableValue>();

            executionContext
                .Setup(x => x.Variables)
                .Returns(new Variables(tc, copy: variables, warnings: out warnings));

            executionContext
                .Setup(x => x.GetScopedEnvironment())
                .Returns(new SystemEnvironment());

            executionContext
                .Setup(x => x.GetVariableValueOrDefault(It.IsAny<string>()))
                .Returns((string variableName) =>
                {
                    var value = variables.GetValueOrDefault(variableName);
                    if (value != null)
                    {
                        return value.Value;
                    }
                    return null;
                });

            return executionContext.Object;
        }

        private Mock<INodeHandlerHelper> GetMockedNodeHandlerHelper()
        {
            // please don't change this method since test rely on the default behavior
            // override the behaviour in specific test instead
            var nodeHandlerHelper = new Mock<INodeHandlerHelper>();

            nodeHandlerHelper
                .Setup(x => x.IsNodeFolderExist(It.IsAny<string>(), It.IsAny<IHostContext>()))
                .Returns(true);

            nodeHandlerHelper
                .Setup(x => x.GetNodeFolderPath(It.IsAny<string>(), It.IsAny<IHostContext>()))
                .Returns((string nodeFolderName, IHostContext hostContext) => Path.Combine(
                    hostContext.GetDirectory(WellKnownDirectory.Externals),
                    nodeFolderName,
                    "bin",
                    $"node{IOUtil.ExeExtension}"));

            nodeHandlerHelper
                .Setup(x => x.GetFilteredPossibleNodeFolders(It.IsAny<string>(), It.IsAny<string[]>()))
                .Returns(Array.Empty<string>);

            return nodeHandlerHelper;
        }
    }
}