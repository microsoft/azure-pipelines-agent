// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies;
using Moq;
using Xunit;
using Agent.Sdk;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    /// <summary>
    /// Tests that the Node10 and Node6 strategies fail fast (return null so the
    /// orchestrator surfaces its clean terminal error) when the corresponding
    /// node binary is not present on disk — e.g. on the pipelines-agent package,
    /// which does not ship node6/node10. This mirrors the on-disk guard already
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

        private Mock<IExecutionContext> CreateExecutionContext(TestHostContext tc)
        {
            var executionContext = new Mock<IExecutionContext>();
            var variables = new Dictionary<string, VariableValue>
            {
                // Force node-selection knobs to deterministic values via RuntimeKnobSource, which
                // reads from the execution context variables and takes precedence over the process
                // environment variable. This avoids cross-test env-var races (other test collections
                // run in parallel and may set these globals).
                { "AGENT_RESTRICT_EOL_NODE_VERSIONS", "false" },
                { "AGENT_USE_NODE20_1", "false" },
                { "AGENT_USE_NODE24", "false" },
                { "AGENT_USE_NODE24_WITH_HANDLER_DATA", "false" }
            };
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
                .Returns(false);

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
