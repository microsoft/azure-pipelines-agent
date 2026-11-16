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
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    [Collection("Unified NodeHandler Tests")]
    public sealed class NodeHandlerL0
    {
        private Mock<INodeHandlerHelper> nodeHandlerHalper;

        public NodeHandlerL0()
        {
            nodeHandlerHalper = GetMockedNodeHandlerHelper();
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(127, false)]
        [InlineData(216, false)]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void NodeExecutable_RequiresSuccessfulVersionProbe(int exitCode, bool expected)
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string binDirectory = Path.Combine(directory, "node24", "bin");
            Directory.CreateDirectory(binDirectory);
            try
            {
                File.WriteAllText(Path.Combine(binDirectory, $"node{IOUtil.ExeExtension}"), string.Empty);
                var processInvoker = new Mock<IProcessInvoker>();
                processInvoker.Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(), It.IsAny<string>(), "-v",
                    It.IsAny<IDictionary<string, string>>(), false,
                    It.IsAny<Encoding>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(exitCode);
                var hostContext = new Mock<IHostContext>();
                hostContext.Setup(x => x.GetDirectory(It.IsAny<WellKnownDirectory>())).Returns(directory);
                hostContext.Setup(x => x.CreateService<IProcessInvoker>()).Returns(processInvoker.Object);

                Assert.Equal(expected, new NodeHandlerHelper().IsNodeExecutable(
                    "node24", hostContext.Object, Mock.Of<IExecutionContext>()));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Theory]
        [InlineData("node20_1", true, false, 1, true, true)]
        [InlineData("node20_1", false, false, 1, true, false)]
        [InlineData("node20_1", false, true, 1, true, false)]
        [InlineData("node20_1", true, false, 1, false, false)]
        [InlineData("node20_1", true, false, 216, true, false)]
        [InlineData("node24", false, true, 1, true, true)]
        [InlineData("node24", false, false, 1, true, false)]
        [InlineData("node24", true, false, 1, true, false)]
        [InlineData("node24", false, true, 1, false, false)]
        [InlineData("node24", false, true, 216, true, false)]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void NodeExecutable_UnsupportedSystemFlagOnlyOverridesGlibc(
            string folder, bool useNode20Override, bool useNode24Override, int exitCode, bool glibcError, bool expected)
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string binDirectory = Path.Combine(directory, folder, "bin");
            Directory.CreateDirectory(binDirectory);
            try
            {
                File.WriteAllText(Path.Combine(binDirectory, $"node{IOUtil.ExeExtension}"), string.Empty);
                using (var tc = CreateTestHostContext())
                {
                    var executionContext = CreateTestExecutionContext(tc, new Dictionary<string, VariableValue>
                    {
                        ["AGENT_USE_NODE20_IN_UNSUPPORTED_SYSTEM"] = useNode20Override.ToString(),
                        ["AGENT_USE_NODE24_IN_UNSUPPORTED_SYSTEM"] = useNode24Override.ToString()
                    });
                    var processInvoker = new Mock<IProcessInvoker>();
                    processInvoker.Setup(x => x.ExecuteAsync(
                        It.IsAny<string>(), It.IsAny<string>(), "-v",
                        It.IsAny<IDictionary<string, string>>(), false,
                        It.IsAny<Encoding>(), It.IsAny<CancellationToken>()))
                        .Callback(() => processInvoker.Raise(x => x.ErrorDataReceived += null, processInvoker.Object,
                            new ProcessDataReceivedEventArgs(glibcError
                                ? "node: libc.so.6: version `GLIBC_2.28' not found"
                                : "node: missing another library")))
                        .ReturnsAsync(exitCode);
                    var hostContext = new Mock<IHostContext>();
                    hostContext.Setup(x => x.GetDirectory(It.IsAny<WellKnownDirectory>())).Returns(directory);
                    hostContext.Setup(x => x.CreateService<IProcessInvoker>()).Returns(processInvoker.Object);

                    Assert.Equal(expected, new NodeHandlerHelper().IsNodeExecutable(folder, hostContext.Object, executionContext));
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNodeForNodeHandlerEnvVarNotSet()
        {
            ResetNodeKnobs();

            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                NodeHandler nodeHandler = new NodeHandler(nodeHandlerHalper.Object);

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc);
                nodeHandler.Data = new NodeHandlerData();

                string actualLocation = nodeHandler.GetNodeLocation(node20ResultsInGlibCError: false, node24ResultsInGlibCError: false, inContainer: false);
                string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                    "node",
                    "bin",
                    $"node{IOUtil.ExeExtension}");
                Assert.Equal(expectedLocation, actualLocation);
            }
        }

        [Theory]
        [InlineData("node")]
        [InlineData("node10")]
        [InlineData("node16")]
        [InlineData("node20_1")]
        [InlineData("node24")]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNewNodeForNewNodeHandler(string nodeVersion)
        {
            ResetNodeKnobs();

            try
            {
                // Use a unique test name per data row to avoid sharing the same trace file across parallel runs
                using (TestHostContext thc = CreateTestHostContext($"{nameof(UseNewNodeForNewNodeHandler)}_{nodeVersion}"))
                {
                    thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                    thc.SetSingleton(new ExtensionManager() as IExtensionManager);
                    var processInvokerMock = new Mock<IProcessInvoker>();
                    for (int i = 0; i < 10; i++)
                    {
                        thc.EnqueueInstance<IProcessInvoker>(processInvokerMock.Object);
                    }
                    SetupNodeProcessInvocation(processInvokerMock, nodeVersion, true);

                    NodeHandler nodeHandler = new NodeHandler(nodeHandlerHalper.Object);

                    nodeHandler.Initialize(thc);
                    nodeHandler.ExecutionContext = CreateTestExecutionContext(thc);
                    nodeHandler.Data = nodeVersion switch
                    {
                        "node" => new NodeHandlerData(),
                        "node10" => new Node10HandlerData(),
                        "node16" => new Node16HandlerData(),
                        "node20_1" => new Node20_1HandlerData(),
                        "node24" => new Node24HandlerData(),
                        _ => throw new Exception("Invalid node version"),
                    };

                    string actualLocation = nodeHandler.GetNodeLocation(node20ResultsInGlibCError: false, node24ResultsInGlibCError: false, inContainer: false);
                    string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                        nodeVersion,
                        "bin",
                        $"node{IOUtil.ExeExtension}");
                    Assert.Equal(expectedLocation, actualLocation);
                }
            }
            finally
            {
                ResetNodeKnobs();
            }
        }

        [Theory]
        [InlineData("node")]
        [InlineData("node10")]
        [InlineData("node16")]
        [InlineData("node20_1")]
        [InlineData("node24")]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ForceUseNode24Knob(string nodeVersion)
        {
            ResetNodeKnobs();

            Environment.SetEnvironmentVariable("AGENT_USE_NODE24", "true");

            try
            {
                // Use a unique test name per data row to avoid sharing the same trace file across parallel runs
                using (TestHostContext thc = CreateTestHostContext($"{nameof(ForceUseNode24Knob)}_{nodeVersion}"))
                {
                    thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                    thc.SetSingleton(new ExtensionManager() as IExtensionManager);
                    var processInvokerMock = new Mock<IProcessInvoker>();
                    for (int i = 0; i < 10; i++)
                    {
                        thc.EnqueueInstance<IProcessInvoker>(processInvokerMock.Object);
                    }
                    SetupNodeProcessInvocation(processInvokerMock, nodeVersion, true);

                    NodeHandler nodeHandler = new NodeHandler(nodeHandlerHalper.Object);

                    nodeHandler.Initialize(thc);
                    nodeHandler.ExecutionContext = CreateTestExecutionContext(thc);
                    nodeHandler.Data = nodeVersion switch
                    {
                        "node" => new NodeHandlerData(),
                        "node10" => new Node10HandlerData(),
                        "node16" => new Node16HandlerData(),
                        "node20_1" => new Node20_1HandlerData(),
                        "node24" => new Node24HandlerData(),
                        _ => throw new Exception("Invalid node version"),
                    };

                    string actualLocation = nodeHandler.GetNodeLocation(node20ResultsInGlibCError: false, node24ResultsInGlibCError: false, inContainer: false);
                    string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                        "node24",
                        "bin",
                        $"node{IOUtil.ExeExtension}");
                    Assert.Equal(expectedLocation, actualLocation);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("AGENT_USE_NODE24", null);
            }
        }

        [Theory]
        [InlineData("node24")]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void Node24NotExecutable(string nodeVersion)
        {
            ResetNodeKnobs();

            try
            {
                // Use a unique test name per data row to avoid sharing the same trace file across parallel runs
                using (TestHostContext thc = CreateTestHostContext($"{nameof(Node24NotExecutable)}_{nodeVersion}"))
                {
                    thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                    thc.SetSingleton(new ExtensionManager() as IExtensionManager);
                    var processInvokerMock = new Mock<IProcessInvoker>();
                    for (int i = 0; i < 10; i++)
                    {
                        thc.EnqueueInstance<IProcessInvoker>(processInvokerMock.Object);
                    }
                    SetupNodeProcessInvocation(processInvokerMock, nodeVersion, false);

                    nodeHandlerHalper
                    .Setup(x => x.IsNodeExecutable(
                        It.Is<string>(folder => folder == "node24"),
                        It.IsAny<IHostContext>(),
                        It.IsAny<IExecutionContext>()))
                    .Returns(false);
                    NodeHandler nodeHandler = new NodeHandler(nodeHandlerHalper.Object);

                    nodeHandler.Initialize(thc);
                    nodeHandler.ExecutionContext = CreateTestExecutionContext(thc);
                    nodeHandler.Data = nodeVersion switch
                    {
                        "node" => new NodeHandlerData(),
                        "node10" => new Node10HandlerData(),
                        "node16" => new Node16HandlerData(),
                        "node20_1" => new Node20_1HandlerData(),
                        "node24" => new Node24HandlerData(),
                        _ => throw new Exception("Invalid node version"),
                    };

                    string actualLocation = nodeHandler.GetNodeLocation(node20ResultsInGlibCError: false, node24ResultsInGlibCError: false, inContainer: false);
                    string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                        "node20_1",
                        "bin",
                        $"node{IOUtil.ExeExtension}");
                    Assert.Equal(expectedLocation, actualLocation);
                }
            }
            finally
            {
                ResetNodeKnobs();
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNode24HandlerByDefault()
        {
            ResetNodeKnobs();

            try
            {
                using (TestHostContext thc = CreateTestHostContext())
                {
                    thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                    thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                    NodeHandler nodeHandler = new NodeHandler(nodeHandlerHalper.Object);

                    nodeHandler.Initialize(thc);
                    nodeHandler.ExecutionContext = CreateTestExecutionContext(thc);
                    nodeHandler.Data = new Node24HandlerData();

                    string actualLocation = nodeHandler.GetNodeLocation(node20ResultsInGlibCError: false, node24ResultsInGlibCError: false, inContainer: false);
                    string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                        "node24",
                        "bin",
                        $"node{IOUtil.ExeExtension}");
                    Assert.Equal(expectedLocation, actualLocation);
                }
            }
            finally
            {
                ResetNodeKnobs();
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNode20OverrideFromEnvironment()
        {
            ResetNodeKnobs();
            var original = Environment.GetEnvironmentVariable("AGENT_USE_NODE20_1");
            Environment.SetEnvironmentVariable("AGENT_USE_NODE20_1", "true");
            try
            {
                using (TestHostContext thc = CreateTestHostContext())
                {
                    thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                    thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                    NodeHandler nodeHandler = new NodeHandler(nodeHandlerHalper.Object);

                    nodeHandler.Initialize(thc);
                    nodeHandler.ExecutionContext = CreateTestExecutionContext(thc);
                    nodeHandler.Data = new Node10HandlerData();

                    string actualLocation = nodeHandler.GetNodeLocation(node20ResultsInGlibCError: false, node24ResultsInGlibCError: false, inContainer: false);
                    string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                        "node20_1",
                        "bin",
                        $"node{IOUtil.ExeExtension}");
                    Assert.Equal(expectedLocation, actualLocation);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("AGENT_USE_NODE20_1", original);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNode20OverrideFromHostContext()
        {
            ResetNodeKnobs();
            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                var variables = new Dictionary<string, VariableValue>();

                variables.Add("AGENT_USE_NODE20_1", new VariableValue("true"));

                NodeHandler nodeHandler = new NodeHandler(nodeHandlerHalper.Object);

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc, variables);
                nodeHandler.Data = new Node10HandlerData();

                string actualLocation = nodeHandler.GetNodeLocation(node20ResultsInGlibCError: false, node24ResultsInGlibCError: false, inContainer: false);
                string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                    "node20_1",
                    "bin",
                    $"node{IOUtil.ExeExtension}");
                Assert.Equal(expectedLocation, actualLocation);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void DisabledNode20OverrideUsesManualRunner()
        {
            ResetNodeKnobs();

            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                var variables = new Dictionary<string, VariableValue>();

                // Explicitly set variable feature flag to false
                variables.Add("AGENT_USE_NODE20_1", new VariableValue("false"));

                NodeHandler nodeHandler = new NodeHandler(nodeHandlerHalper.Object);

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc, variables);
                nodeHandler.Data = new Node10HandlerData();

                string actualLocation = nodeHandler.GetNodeLocation(node20ResultsInGlibCError: false, node24ResultsInGlibCError: false, inContainer: false);
                string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                    "node10",
                    "bin",
                    $"node{IOUtil.ExeExtension}");
                Assert.Equal(expectedLocation, actualLocation);
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void Node20TaskDoesNotFallbackToNode16InLegacySelection(bool node20Available, bool node20ResultsInGlibCError)
        {
            ResetNodeKnobs();

            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                var variables = new Dictionary<string, VariableValue>
                {
                    ["AGENT_USE_ENHANCED_NODE_SELECTION"] = new VariableValue("false"),
                    ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = new VariableValue("false")
                };
                var helper = GetMockedNodeHandlerHelper();
                helper
                    .Setup(x => x.IsNodeFolderExist(NodeHandler.Node20_1Folder, It.IsAny<IHostContext>()))
                    .Returns(node20Available);
                helper
                    .Setup(x => x.IsNodeFolderExist(NodeHandler.Node16Folder, It.IsAny<IHostContext>()))
                    .Returns(true);

                var nodeHandler = new NodeHandler(helper.Object);
                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc, variables);
                nodeHandler.Data = new Node20_1HandlerData();

                NotSupportedException exception = Assert.Throws<NotSupportedException>(
                    () => nodeHandler.GetNodeLocation(node20ResultsInGlibCError, node24ResultsInGlibCError: true, inContainer: false));
                Assert.Equal(StringUtil.Loc("NodeVersionNotAvailable", nameof(Node20_1HandlerData)), exception.Message);
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void Node20TaskUsesGlobalNode24OverrideWhenNode20IsUnavailable(bool node20Available, bool node20ResultsInGlibCError)
        {
            ResetNodeKnobs();

            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                var variables = new Dictionary<string, VariableValue>
                {
                    ["AGENT_USE_ENHANCED_NODE_SELECTION"] = new VariableValue("false"),
                    ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = new VariableValue("false"),
                    ["AGENT_USE_NODE24"] = new VariableValue("true")
                };
                var helper = GetMockedNodeHandlerHelper();
                helper
                    .Setup(x => x.IsNodeFolderExist(NodeHandler.Node20_1Folder, It.IsAny<IHostContext>()))
                    .Returns(node20Available);
                helper
                    .Setup(x => x.IsNodeFolderExist(NodeHandler.Node16Folder, It.IsAny<IHostContext>()))
                    .Returns(true);

                var nodeHandler = new NodeHandler(helper.Object);
                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc, variables);
                nodeHandler.Data = new Node20_1HandlerData();

                string actualLocation = nodeHandler.GetNodeLocation(node20ResultsInGlibCError, node24ResultsInGlibCError: false, inContainer: false);
                string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals), "node24", "bin", $"node{IOUtil.ExeExtension}");

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
                    .Returns(new string[] { "node20_1" });

                var variables = new Dictionary<string, VariableValue>();

                variables.Add("AGENT_USE_NODE", new VariableValue("lts"));

                NodeHandler nodeHandler = new NodeHandler(mockedNodeHandlerHelper.Object);

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc, variables);
                nodeHandler.Data = new Node10HandlerData();

                string actualLocation = nodeHandler.GetNodeLocation(node20ResultsInGlibCError: false, node24ResultsInGlibCError: false, inContainer: false);
                string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                    "node24",
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

                Assert.Throws<FileNotFoundException>(() => nodeHandler.GetNodeLocation(node20ResultsInGlibCError: false, node24ResultsInGlibCError: false, inContainer: false));
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

                Assert.Throws<FileNotFoundException>(() => nodeHandler.GetNodeLocation(node20ResultsInGlibCError: false, node24ResultsInGlibCError: false, inContainer: false));
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

                string actualLocation = nodeHandler.GetNodeLocation(node20ResultsInGlibCError: false, node24ResultsInGlibCError: false, inContainer: false);
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

                string actualLocation = nodeHandler.GetNodeLocation(node20ResultsInGlibCError: false, node24ResultsInGlibCError: false, inContainer: false);
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

                Assert.Throws<FileNotFoundException>(() => nodeHandler.GetNodeLocation(node20ResultsInGlibCError: false, node24ResultsInGlibCError: false, inContainer: false));
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

            nodeHandlerHelper
                .Setup(x => x.IsNodeExecutable(It.IsAny<string>(), It.IsAny<IHostContext>(), It.IsAny<IExecutionContext>()))
                .Returns(true);

            return nodeHandlerHelper;
        }

        private void SetupNodeProcessInvocation(Mock<IProcessInvoker> processInvokerMock, string nodeFolder, bool node24Executable)
        {
            string nodeExePath = Path.Combine("externals", nodeFolder, "bin", $"node{IOUtil.ExeExtension}");

            processInvokerMock.Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.Is<string>(fileName => fileName.Contains(nodeExePath)),
                    "-v",
                    It.IsAny<IDictionary<string, string>>(),
                    false,
                    It.IsAny<Encoding>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(node24Executable ? 0 : 216);
        }

        private void ResetNodeKnobs()
        {
            Environment.SetEnvironmentVariable("AGENT_USE_NODE20_1", null);
            Environment.SetEnvironmentVariable("AGENT_USE_NODE20_IN_UNSUPPORTED_SYSTEM", null);
            Environment.SetEnvironmentVariable("AGENT_USE_NODE24", null);
            Environment.SetEnvironmentVariable("AGENT_USE_NODE24_IN_UNSUPPORTED_SYSTEM", null);
        }
    }
}
