// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies;
using Moq;
using Xunit;
using Agent.Sdk;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    /// <summary>
    /// Tests that legacy strategies fail fast (return null so the
    /// orchestrator surfaces its clean terminal error) when the corresponding
    /// node binary is not present on disk. Neither package bundles legacy runners.
    /// This mirrors the on-disk guard already
    /// present in Node24Strategy/Node16Strategy and avoids returning a
    /// non-existent node path that would otherwise fail later at process launch.
    /// </summary>
    [Collection("Unified NodeHandler Tests")]
    public sealed class NodeStrategyFallbackL0
    {
        private void ClearEolKnob()
        {
            // Ensure the EOL-policy knob is not accidentally enabled by the host environment.
            Environment.SetEnvironmentVariable("AGENT_RESTRICT_EOL_NODE_VERSIONS", null);
        }

        private Mock<IExecutionContext> CreateExecutionContext(
            TestHostContext tc,
            Dictionary<string, VariableValue> variableOverrides = null)
        {
            var variables = new Dictionary<string, VariableValue>
            {
                // Force node-selection knobs to deterministic values via RuntimeKnobSource, which
                // reads from the execution context variables and takes precedence over the process
                // environment variable. This avoids cross-test env-var races (other test collections
                // run in parallel and may set these globals).
                { "AGENT_RESTRICT_EOL_NODE_VERSIONS", "false" },
                { "AGENT_USE_NODE20_1", "false" },
                { "AGENT_USE_NODE24", "false" }
            };
            foreach (var variableOverride in variableOverrides ?? new Dictionary<string, VariableValue>())
            {
                variables[variableOverride.Key] = variableOverride.Value;
            }

            var executionContext = new Mock<IExecutionContext>();
            List<string> warnings;

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
                    if (variables.TryGetValue(variableName, out VariableValue value))
                    {
                        return value.Value;
                    }
                    return null;
                });

            executionContext
                .Setup(x => x.GetHostContext())
                .Returns(tc);

            return executionContext;
        }

        private Mock<INodeHandlerHelper> CreateNodeHandlerHelper(bool nodeFolderExists)
        {
            var helper = new Mock<INodeHandlerHelper>();

            helper
                .Setup(x => x.IsNodeFolderExist(It.IsAny<string>(), It.IsAny<IHostContext>()))
                .Returns(nodeFolderExists);

            helper
                .Setup(x => x.GetNodeFolderPath(It.IsAny<string>(), It.IsAny<IHostContext>()))
                .Returns((string nodeFolderName, IHostContext hostContext) => Path.Combine(
                    hostContext.GetDirectory(WellKnownDirectory.Externals),
                    nodeFolderName,
                    "bin",
                    $"node{IOUtil.ExeExtension}"));

            // node24 executable check: report not executable so Node24Strategy skips in orchestrator tests.
            helper
                .Setup(x => x.IsNodeExecutable(It.IsAny<string>(), It.IsAny<IHostContext>(), It.IsAny<IExecutionContext>()))
                .Returns((string folder, IHostContext _, IExecutionContext _) =>
                    nodeFolderExists && folder == NodeHandler.Node20_1Folder);

            return helper;
        }

        private Mock<IGlibcCompatibilityInfoProvider> CreateGlibcProvider()
        {
            var glibcMock = new Mock<IGlibcCompatibilityInfoProvider>();
            glibcMock.Setup(x => x.Initialize(It.IsAny<IHostContext>()));
            glibcMock
                .Setup(x => x.GetGlibcCompatibilityAsync(It.IsAny<TaskContext>(), It.IsAny<IExecutionContext>()))
                .ReturnsAsync(GlibcCompatibilityInfo.Compatible);
            glibcMock
                .Setup(x => x.CheckGlibcCompatibilityAsync(It.IsAny<IExecutionContext>()))
                .ReturnsAsync(GlibcCompatibilityInfo.Compatible);
            return glibcMock;
        }

        [Theory]
        [InlineData(false, 20, true, true, false, false, false, 24)]
        [InlineData(true, 20, true, true, false, false, false, 24)]
        [InlineData(false, 24, true, true, false, false, false, 24)]
        [InlineData(true, 24, true, true, false, false, false, 24)]
        [InlineData(false, 20, false, true, false, false, false, 20)]
        [InlineData(true, 20, false, true, false, false, false, 20)]
        [InlineData(false, 24, false, true, false, false, false, 20)]
        [InlineData(true, 24, false, true, false, false, false, 20)]
        [InlineData(false, 20, true, false, false, false, false, 24)]
        [InlineData(true, 20, true, false, false, false, false, 24)]
        [InlineData(false, 20, true, true, true, false, false, 20)]
        [InlineData(true, 20, true, true, true, false, false, 20)]
        [InlineData(false, 24, false, false, false, false, false, 0)]
        [InlineData(true, 24, false, false, false, false, false, 0)]
        [InlineData(false, 20, true, false, false, true, false, 0)]
        [InlineData(true, 20, true, false, false, true, false, 0)]
        [InlineData(false, 16, true, false, false, true, false, 0)]
        [InlineData(true, 16, true, false, false, true, false, 0)]
        [InlineData(false, 24, true, true, false, true, false, 20)]
        [InlineData(true, 24, true, true, false, true, false, 20)]
        [InlineData(false, 6, true, true, false, false, false, 6)]
        [InlineData(true, 6, true, true, false, false, false, 6)]
        [InlineData(false, 10, true, true, false, false, false, 10)]
        [InlineData(true, 10, true, true, false, false, false, 10)]
        [InlineData(false, 16, true, true, false, false, false, 16)]
        [InlineData(true, 16, true, true, false, false, false, 16)]
        [InlineData(false, 16, true, true, false, false, true, 24)]
        [InlineData(true, 16, true, true, false, false, true, 24)]
        [InlineData(false, 24, true, true, false, true, true, 20)]
        [InlineData(true, 24, true, true, false, true, true, 20)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void NodeSelection_PrefersNode24_PreservesLegacyHandlersAndStrictNode20Pin(
            bool useStrategy, int handlerVersion, bool node24Available, bool node20Available,
            bool node24GlibcError, bool pinNode20, bool eolPolicy, int expectedVersion)
        {
            using (var thc = new TestHostContext(this))
            {
                thc.SetSingleton<IWorkerCommandManager>(new WorkerCommandManager());
                thc.SetSingleton<IExtensionManager>(new ExtensionManager());
                var glibcProvider = CreateGlibcProvider();
                glibcProvider
                    .Setup(x => x.GetGlibcCompatibilityAsync(It.IsAny<TaskContext>(), It.IsAny<IExecutionContext>()))
                    .ReturnsAsync(GlibcCompatibilityInfo.Create(node24GlibcError, false));
                thc.SetSingleton<IGlibcCompatibilityInfoProvider>(glibcProvider.Object);
                var executionContext = CreateExecutionContext(thc, new Dictionary<string, VariableValue>
                {
                    ["AGENT_USE_ENHANCED_NODE_SELECTION"] = useStrategy.ToString(),
                    ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = eolPolicy.ToString(),
                    ["AGENT_USE_NODE20_1"] = pinNode20.ToString(),
                    ["AGENT_USE_NODE"] = "upgrade"
                });
                var helper = CreateNodeHandlerHelper(nodeFolderExists: true);
                helper.Setup(x => x.IsNodeFolderExist(NodeHandler.Node24Folder, It.IsAny<IHostContext>()))
                    .Returns(node24Available);
                helper.Setup(x => x.IsNodeFolderExist(NodeHandler.Node20_1Folder, It.IsAny<IHostContext>()))
                    .Returns(node20Available);
                helper.Setup(x => x.IsNodeExecutable(It.IsAny<string>(), It.IsAny<IHostContext>(), It.IsAny<IExecutionContext>()))
                    .Returns((string folder, IHostContext _, IExecutionContext _) =>
                        folder == NodeHandler.Node24Folder ? node24Available : node20Available);

                var handler = new NodeHandler(helper.Object)
                {
                    ExecutionContext = executionContext.Object,
                    Data = handlerVersion switch
                    {
                        6 => new NodeHandlerData(),
                        10 => new Node10HandlerData(),
                        16 => new Node16HandlerData(),
                        20 => new Node20_1HandlerData(),
                        24 => new Node24HandlerData(),
                        _ => throw new ArgumentOutOfRangeException(nameof(handlerVersion))
                    }
                };
                handler.Initialize(thc);

                if (expectedVersion == 0)
                {
                    Assert.Throws<NotSupportedException>(() => handler.GetNodeLocation(false, node24GlibcError, false));
                }
                else
                {
                    string folder = expectedVersion switch
                    {
                        6 => "node",
                        20 => "node20_1",
                        _ => $"node{expectedVersion}"
                    };
                    Assert.Equal(helper.Object.GetNodeFolderPath(folder, thc),
                        handler.GetNodeLocation(false, node24GlibcError, false));
                }

                if (pinNode20 && node20Available)
                {
                    glibcProvider
                        .Setup(x => x.GetGlibcCompatibilityAsync(It.IsAny<TaskContext>(), It.IsAny<IExecutionContext>()))
                        .ReturnsAsync(GlibcCompatibilityInfo.Create(node24GlibcError, true));
                    Assert.Throws<NotSupportedException>(() => handler.GetNodeLocation(true, node24GlibcError, false));

                    glibcProvider
                        .Setup(x => x.GetGlibcCompatibilityAsync(It.IsAny<TaskContext>(), It.IsAny<IExecutionContext>()))
                        .ReturnsAsync(GlibcCompatibilityInfo.Create(node24GlibcError, false));
                    helper.Setup(x => x.IsNodeExecutable(NodeHandler.Node20_1Folder, It.IsAny<IHostContext>(), It.IsAny<IExecutionContext>()))
                        .Returns(false);
                    Assert.Throws<NotSupportedException>(() => handler.GetNodeLocation(false, node24GlibcError, false));
                }
            }
        }

        [Theory]
        [InlineData(false, false, true, false, false, 0)]
        [InlineData(true, false, true, false, false, 0)]
        [InlineData(false, true, true, false, false, 0)]
        [InlineData(true, true, true, false, false, 0)]
        [InlineData(false, true, false, true, false, 0)]
        [InlineData(true, true, false, true, false, 0)]
        [InlineData(false, true, true, true, true, 0)]
        [InlineData(true, true, true, true, true, 0)]
        [InlineData(false, true, true, true, false, 20)]
        [InlineData(true, true, true, true, false, 20)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Node20UnsupportedSystemOverride_DoesNotBypassSelectionChecks(
            bool useStrategy, bool useUnsupportedOverride, bool node20Exists, bool node20Executable,
            bool node20GlibcError, int expectedVersion)
        {
            using (var thc = new TestHostContext(this))
            {
                thc.SetSingleton<IWorkerCommandManager>(new WorkerCommandManager());
                thc.SetSingleton<IExtensionManager>(new ExtensionManager());
                var glibcProvider = CreateGlibcProvider();
                glibcProvider
                    .Setup(x => x.GetGlibcCompatibilityAsync(It.IsAny<TaskContext>(), It.IsAny<IExecutionContext>()))
                    .ReturnsAsync(GlibcCompatibilityInfo.Create(false, node20GlibcError));
                thc.SetSingleton<IGlibcCompatibilityInfoProvider>(glibcProvider.Object);

                var executionContext = CreateExecutionContext(thc, new Dictionary<string, VariableValue>
                {
                    ["AGENT_USE_ENHANCED_NODE_SELECTION"] = useStrategy.ToString(),
                    ["AGENT_USE_NODE20_IN_UNSUPPORTED_SYSTEM"] = useUnsupportedOverride.ToString(),
                    ["AGENT_USE_NODE20_1"] = "true"
                });
                var helper = CreateNodeHandlerHelper(nodeFolderExists: true);
                helper.Setup(x => x.IsNodeFolderExist(NodeHandler.Node24Folder, It.IsAny<IHostContext>()))
                    .Returns(true);
                helper.Setup(x => x.IsNodeFolderExist(NodeHandler.Node20_1Folder, It.IsAny<IHostContext>()))
                    .Returns(node20Exists);
                helper.Setup(x => x.IsNodeExecutable(NodeHandler.Node24Folder, It.IsAny<IHostContext>(), It.IsAny<IExecutionContext>()))
                    .Returns(true);
                helper.Setup(x => x.IsNodeExecutable(NodeHandler.Node20_1Folder, It.IsAny<IHostContext>(), It.IsAny<IExecutionContext>()))
                    .Returns(node20Executable);

                var handler = new NodeHandler(helper.Object)
                {
                    ExecutionContext = executionContext.Object,
                    Data = new Node20_1HandlerData()
                };
                handler.Initialize(thc);

                if (expectedVersion != 0)
                {
                    Assert.Equal(helper.Object.GetNodeFolderPath(NodeHandler.Node20_1Folder, thc),
                        handler.GetNodeLocation(node20GlibcError, false, false));
                }
                else
                {
                    Assert.Throws<NotSupportedException>(() => handler.GetNodeLocation(node20GlibcError, false, false));
                }
            }
        }

        [Theory]
        [InlineData(false, false, true, false, false, 20)]
        [InlineData(true, false, true, false, false, 20)]
        [InlineData(false, true, true, false, false, 20)]
        [InlineData(true, true, true, false, false, 20)]
        [InlineData(false, true, false, true, false, 20)]
        [InlineData(true, true, false, true, false, 20)]
        [InlineData(false, true, true, true, true, 20)]
        [InlineData(true, true, true, true, true, 20)]
        [InlineData(false, true, true, true, false, 24)]
        [InlineData(true, true, true, true, false, 24)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Node24UnsupportedSystemOverride_DoesNotBypassSelectionChecks(
            bool useStrategy, bool useUnsupportedOverride, bool node24Exists, bool node24Executable,
            bool node24GlibcError, int expectedVersion)
        {
            using (var thc = new TestHostContext(this))
            {
                thc.SetSingleton<IWorkerCommandManager>(new WorkerCommandManager());
                thc.SetSingleton<IExtensionManager>(new ExtensionManager());
                var glibcProvider = CreateGlibcProvider();
                glibcProvider
                    .Setup(x => x.GetGlibcCompatibilityAsync(It.IsAny<TaskContext>(), It.IsAny<IExecutionContext>()))
                    .ReturnsAsync(GlibcCompatibilityInfo.Create(node24GlibcError, false));
                thc.SetSingleton<IGlibcCompatibilityInfoProvider>(glibcProvider.Object);

                var executionContext = CreateExecutionContext(thc, new Dictionary<string, VariableValue>
                {
                    ["AGENT_USE_ENHANCED_NODE_SELECTION"] = useStrategy.ToString(),
                    ["AGENT_USE_NODE24_IN_UNSUPPORTED_SYSTEM"] = useUnsupportedOverride.ToString()
                });
                var helper = CreateNodeHandlerHelper(nodeFolderExists: true);
                helper.Setup(x => x.IsNodeFolderExist(NodeHandler.Node24Folder, It.IsAny<IHostContext>()))
                    .Returns(node24Exists);
                helper.Setup(x => x.IsNodeExecutable(NodeHandler.Node24Folder, It.IsAny<IHostContext>(), It.IsAny<IExecutionContext>()))
                    .Returns(node24Executable);

                var handler = new NodeHandler(helper.Object)
                {
                    ExecutionContext = executionContext.Object,
                    Data = new Node24HandlerData()
                };
                handler.Initialize(thc);

                string folder = expectedVersion == 24 ? NodeHandler.Node24Folder : NodeHandler.Node20_1Folder;
                Assert.Equal(helper.Object.GetNodeFolderPath(folder, thc),
                    handler.GetNodeLocation(false, node24GlibcError, false));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Node16Strategy_ReturnsNull_WhenNode16BinaryAbsent()
        {
            using (TestHostContext thc = new TestHostContext(this))
            {
                var executionContext = CreateExecutionContext(thc);
                var helper = CreateNodeHandlerHelper(nodeFolderExists: false);
                var strategy = new Node16Strategy(helper.Object);
                var context = new TaskContext { HandlerData = new Node16HandlerData() };

                Assert.Null(strategy.CanHandle(context, executionContext.Object, GlibcCompatibilityInfo.Compatible));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Node16Strategy_ReturnsNode16_WhenManuallyInstalled()
        {
            using (TestHostContext thc = new TestHostContext(this))
            {
                var executionContext = CreateExecutionContext(thc);
                var helper = CreateNodeHandlerHelper(nodeFolderExists: true);
                var strategy = new Node16Strategy(helper.Object);
                var context = new TaskContext { HandlerData = new Node16HandlerData() };

                Assert.Equal(NodeVersion.Node16,
                    strategy.CanHandle(context, executionContext.Object, GlibcCompatibilityInfo.Compatible).NodeVersion);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Node10Strategy_ReturnsNull_WhenNode10BinaryAbsent()
        {
            ClearEolKnob();
            using (TestHostContext thc = new TestHostContext(this))
            {
                var executionContext = CreateExecutionContext(thc);
                var helper = CreateNodeHandlerHelper(nodeFolderExists: false);
                var strategy = new Node10Strategy(helper.Object);
                var context = new TaskContext { HandlerData = new Node10HandlerData() };

                var result = strategy.CanHandle(context, executionContext.Object, GlibcCompatibilityInfo.Compatible);

                Assert.Null(result);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Node10Strategy_ReturnsNode10_WhenNode10BinaryPresent()
        {
            ClearEolKnob();
            using (TestHostContext thc = new TestHostContext(this))
            {
                var executionContext = CreateExecutionContext(thc);
                var helper = CreateNodeHandlerHelper(nodeFolderExists: true);
                var strategy = new Node10Strategy(helper.Object);
                var context = new TaskContext { HandlerData = new Node10HandlerData() };

                var result = strategy.CanHandle(context, executionContext.Object, GlibcCompatibilityInfo.Compatible);

                Assert.NotNull(result);
                Assert.Equal(NodeVersion.Node10, result.NodeVersion);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Node6Strategy_ReturnsNull_WhenNode6BinaryAbsent()
        {
            ClearEolKnob();
            using (TestHostContext thc = new TestHostContext(this))
            {
                var executionContext = CreateExecutionContext(thc);
                var helper = CreateNodeHandlerHelper(nodeFolderExists: false);
                var strategy = new Node6Strategy(helper.Object);
                var context = new TaskContext { HandlerData = new NodeHandlerData() };

                var result = strategy.CanHandle(context, executionContext.Object, GlibcCompatibilityInfo.Compatible);

                Assert.Null(result);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Node6Strategy_ReturnsNode6_WhenNode6BinaryPresent()
        {
            ClearEolKnob();
            using (TestHostContext thc = new TestHostContext(this))
            {
                var executionContext = CreateExecutionContext(thc);
                var helper = CreateNodeHandlerHelper(nodeFolderExists: true);
                var strategy = new Node6Strategy(helper.Object);
                var context = new TaskContext { HandlerData = new NodeHandlerData() };

                var result = strategy.CanHandle(context, executionContext.Object, GlibcCompatibilityInfo.Compatible);

                Assert.NotNull(result);
                Assert.Equal(NodeVersion.Node6, result.NodeVersion);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Node20Strategy_ReturnsNull_WhenNode20BinaryAbsent()
        {
            ClearEolKnob();
            using (TestHostContext thc = new TestHostContext(this))
            {
                var executionContext = CreateExecutionContext(thc);
                var helper = CreateNodeHandlerHelper(nodeFolderExists: false);
                var strategy = new Node20Strategy(helper.Object);
                var context = new TaskContext { HandlerData = new Node20_1HandlerData() };

                var result = strategy.CanHandle(context, executionContext.Object, GlibcCompatibilityInfo.Compatible);

                Assert.Null(result);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Node20Strategy_ReturnsNode20_WhenNode20BinaryPresent()
        {
            ClearEolKnob();
            using (TestHostContext thc = new TestHostContext(this))
            {
                var executionContext = CreateExecutionContext(thc);
                var helper = CreateNodeHandlerHelper(nodeFolderExists: true);
                var strategy = new Node20Strategy(helper.Object);
                var context = new TaskContext { HandlerData = new Node20_1HandlerData() };

                var result = strategy.CanHandle(context, executionContext.Object, GlibcCompatibilityInfo.Compatible);

                Assert.NotNull(result);
                Assert.Equal(NodeVersion.Node20, result.NodeVersion);
            }
        }

        [Theory]
        [InlineData("DistributedTask.Agent.WarnOnNode20Task", "false", false)]
        [InlineData("DistributedTask.Agent.WarnOnNode20Task", "true", true)]
        [InlineData("AGENT_WARN_ON_NODE20_TASK", "true", true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Node20Strategy_GatesNode20TaskWarning(string knobName, string knobValue, bool expectWarning)
        {
            ClearEolKnob();
            using (TestHostContext thc = new TestHostContext(this))
            {
                var variables = new Dictionary<string, VariableValue>
                {
                    { knobName, knobValue }
                };
                var executionContext = CreateExecutionContext(thc, variables);
                var helper = CreateNodeHandlerHelper(nodeFolderExists: true);
                var strategy = new Node20Strategy(helper.Object);
                var context = new TaskContext { HandlerData = new Node20_1HandlerData() };

                var result = strategy.CanHandle(context, executionContext.Object, GlibcCompatibilityInfo.Compatible);

                Assert.NotNull(result);
                Assert.Equal(expectWarning, !string.IsNullOrEmpty(result.Warning));
                if (expectWarning)
                {
                    Assert.Contains("Node20TaskRetirementWarning", result.Warning);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Node20Strategy_DoesNotWarnForRedirectedNode16Task()
        {
            ClearEolKnob();
            using (TestHostContext thc = new TestHostContext(this))
            {
                var variables = new Dictionary<string, VariableValue>
                {
                    { "AGENT_USE_NODE20_1", "true" },
                    { "DistributedTask.Agent.WarnOnNode20Task", "true" }
                };
                var executionContext = CreateExecutionContext(thc, variables);
                var helper = CreateNodeHandlerHelper(nodeFolderExists: true);
                var strategy = new Node20Strategy(helper.Object);
                var context = new TaskContext { HandlerData = new Node16HandlerData() };

                var result = strategy.CanHandle(context, executionContext.Object, GlibcCompatibilityInfo.Compatible);

                Assert.NotNull(result);
                Assert.Null(result.Warning);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task Orchestrator_HostSelection_FailsFast_WhenNode16TaskAndNoNodeAvailable()
        {
            using (TestHostContext thc = new TestHostContext(this))
            {
                thc.SetSingleton<IGlibcCompatibilityInfoProvider>(CreateGlibcProvider().Object);
                var executionContext = CreateExecutionContext(thc);
                var helper = CreateNodeHandlerHelper(nodeFolderExists: false);
                var orchestrator = new NodeVersionOrchestrator(executionContext.Object, thc, helper.Object);
                var context = new TaskContext { HandlerData = new Node16HandlerData() };

                await Assert.ThrowsAsync<NotSupportedException>(() => orchestrator.SelectNodeVersionForHostAsync(context));
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task Orchestrator_HostSelection_DoesNotFallbackFromNode20TaskToNode16(bool node20Exists, bool node20HasGlibcError)
        {
            using (TestHostContext thc = new TestHostContext(this))
            {
                var glibcProvider = CreateGlibcProvider();
                glibcProvider
                    .Setup(x => x.GetGlibcCompatibilityAsync(It.IsAny<TaskContext>(), It.IsAny<IExecutionContext>()))
                    .ReturnsAsync(GlibcCompatibilityInfo.Create(
                        node24HasGlibcError: false,
                        node20HasGlibcError: node20HasGlibcError));
                thc.SetSingleton<IGlibcCompatibilityInfoProvider>(glibcProvider.Object);

                var executionContext = CreateExecutionContext(thc);
                var helper = CreateNodeHandlerHelper(nodeFolderExists: node20Exists);
                helper
                    .Setup(x => x.IsNodeFolderExist(NodeVersionHelper.GetFolderName(NodeVersion.Node16), It.IsAny<IHostContext>()))
                    .Returns(true);
                var orchestrator = new NodeVersionOrchestrator(executionContext.Object, thc, helper.Object);
                var context = new TaskContext { HandlerData = new Node20_1HandlerData() };

                await Assert.ThrowsAsync<NotSupportedException>(
                    () => orchestrator.SelectNodeVersionForHostAsync(context));
            }
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Orchestrator_ContainerSelection_FallsBackToManuallyInstalledNode16(bool node16Installed, bool eolPolicyEnabled)
        {
            using (TestHostContext thc = new TestHostContext(this))
            {
                thc.SetSingleton<IGlibcCompatibilityInfoProvider>(CreateGlibcProvider().Object);
                var executionContext = CreateExecutionContext(thc, new Dictionary<string, VariableValue>
                {
                    { "AGENT_RESTRICT_EOL_NODE_VERSIONS", eolPolicyEnabled ? "true" : "false" }
                });
                var helper = CreateNodeHandlerHelper(nodeFolderExists: node16Installed);
                var orchestrator = new NodeVersionOrchestrator(executionContext.Object, thc, helper.Object);
                var context = new TaskContext
                {
                    HandlerData = new Node20_1HandlerData(),
                    Container = new ContainerInfo
                    {
                        ContainerId = "test_container",
                        ImageOS = PlatformUtil.HostOS
                    }
                };

                var dockerManager = new Mock<IDockerCommandManager>();
                if (PlatformUtil.RunningOnMacOS)
                {
                    var result = orchestrator.SelectNodeVersionForContainer(context, dockerManager.Object);
                    Assert.Equal(NodeVersion.ContainerDefaultNode, result.NodeVersion);
                    Assert.Equal("node", result.NodePath);
                }
                else if (node16Installed && !eolPolicyEnabled)
                {
                    var result = orchestrator.SelectNodeVersionForContainer(context, dockerManager.Object);
                    Assert.Equal(NodeVersion.Node16, result.NodeVersion);
                    Assert.Contains(NodeHandler.Node16Folder, result.NodePath);
                }
                else
                {
                    Assert.Throws<NotSupportedException>(
                        () => orchestrator.SelectNodeVersionForContainer(context, dockerManager.Object));
                }
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task Orchestrator_DeprecatedRunnerWarning_UsesSharedKnob(bool enabled)
        {
            using (TestHostContext thc = new TestHostContext(this))
            {
                thc.SetSingleton<IGlibcCompatibilityInfoProvider>(CreateGlibcProvider().Object);
                var executionContext = CreateExecutionContext(thc, new Dictionary<string, VariableValue>
                {
                    { "VSTSAGENT_ENABLE_NODE_WARNINGS", enabled.ToString() }
                });
                var helper = CreateNodeHandlerHelper(nodeFolderExists: true);
                var orchestrator = new NodeVersionOrchestrator(executionContext.Object, thc, helper.Object);
                var context = new TaskContext { HandlerData = new Node10HandlerData() };

                var result = await orchestrator.SelectNodeVersionForHostAsync(context);
                Assert.Equal(NodeVersion.Node10, result.NodeVersion);
                Assert.Contains("NodeEOLRetirementWarning", result.Warning);
                Assert.Equal(enabled, result.Warning.Contains("DeprecatedRunner"));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task Orchestrator_HostSelection_FailsFast_WhenNode10TaskAndNoNodeAvailable()
        {
            ClearEolKnob();
            using (TestHostContext thc = new TestHostContext(this))
            {
                thc.SetSingleton<IGlibcCompatibilityInfoProvider>(CreateGlibcProvider().Object);

                var executionContext = CreateExecutionContext(thc);
                var helper = CreateNodeHandlerHelper(nodeFolderExists: false);
                var orchestrator = new NodeVersionOrchestrator(executionContext.Object, thc, helper.Object);
                var context = new TaskContext { HandlerData = new Node10HandlerData() };

                // With node10/node16/node6 absent and node24 not executable, no strategy
                // can handle the Node10 task, so the orchestrator throws its clean terminal error.
                await Assert.ThrowsAsync<NotSupportedException>(
                    () => orchestrator.SelectNodeVersionForHostAsync(context));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task Orchestrator_HostSelection_FailsFast_WhenNode6TaskAndNoNodeAvailable()
        {
            ClearEolKnob();
            using (TestHostContext thc = new TestHostContext(this))
            {
                thc.SetSingleton<IGlibcCompatibilityInfoProvider>(CreateGlibcProvider().Object);

                var executionContext = CreateExecutionContext(thc);
                var helper = CreateNodeHandlerHelper(nodeFolderExists: false);
                var orchestrator = new NodeVersionOrchestrator(executionContext.Object, thc, helper.Object);
                var context = new TaskContext { HandlerData = new NodeHandlerData() };

                await Assert.ThrowsAsync<NotSupportedException>(
                    () => orchestrator.SelectNodeVersionForHostAsync(context));
            }
        }
    }
}
