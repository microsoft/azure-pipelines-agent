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

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class NodeHandlerL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNodeForNodeHandlerEnvVarNotSet()
        {
            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                NodeHandler nodeHandler = new NodeHandler();

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc);
                nodeHandler.Data = new NodeHandlerData();

                string actualLocation = nodeHandler.GetNodeLocation();
                string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                    "node",
                    "bin",
                    $"node{IOUtil.ExeExtension}");
                Assert.Equal(expectedLocation, actualLocation);
            }
        }

        [Theory]
        [InlineData("node10")]
        [InlineData("node14")]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNewNodeForNewNodeHandler(string nodeVersion)
        {
            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                NodeHandler nodeHandler = new NodeHandler();

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc);
                nodeHandler.Data = nodeVersion == "node14" ? (BaseNodeHandlerData) new Node14HandlerData() :  (BaseNodeHandlerData) new Node10HandlerData();

                string actualLocation = nodeHandler.GetNodeLocation();
                string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                    nodeVersion,
                    "bin",
                    $"node{IOUtil.ExeExtension}");
                Assert.Equal(expectedLocation, actualLocation);
            }
        }

        [Theory]
        [InlineData("node10")]
        [InlineData("node14")]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNewNodeForNodeHandlerEnvVarSet(string nodeVersion)
        {
            try
            {
                Environment.SetEnvironmentVariable(nodeVersion == "node14" ? "AGENT_USE_NODE14" : "AGENT_USE_NODE10", "true");

                using (TestHostContext thc = CreateTestHostContext())
                {
                    thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                    thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                    NodeHandler nodeHandler = new NodeHandler();

                    nodeHandler.Initialize(thc);
                    nodeHandler.ExecutionContext = CreateTestExecutionContext(thc);
                    nodeHandler.Data = nodeVersion == "node14" ? (BaseNodeHandlerData) new Node14HandlerData() :  (BaseNodeHandlerData) new Node10HandlerData();

                    string actualLocation = nodeHandler.GetNodeLocation();
                    string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                        nodeVersion,
                        "bin",
                        $"node{IOUtil.ExeExtension}");
                    Assert.Equal(expectedLocation, actualLocation);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(nodeVersion == "node14" ? "AGENT_USE_NODE14" : "AGENT_USE_NODE10", null);
            }
        }

        [Theory]
        [InlineData("node10")]
        [InlineData("node14")]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNewNodeForNodeHandlerHostContextVarSet(string nodeVersion)
        {
            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                var variables = new Dictionary<string, VariableValue>();

                variables.Add(nodeVersion == "node14" ? "AGENT_USE_NODE14" : "AGENT_USE_NODE10", new VariableValue("true"));

                NodeHandler nodeHandler = new NodeHandler();

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc, variables);
                nodeHandler.Data = nodeVersion == "node14" ? (BaseNodeHandlerData) new Node14HandlerData() :  (BaseNodeHandlerData) new Node10HandlerData();

                string actualLocation = nodeHandler.GetNodeLocation();
                string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                    nodeVersion,
                    "bin",
                    $"node{IOUtil.ExeExtension}");
                Assert.Equal(expectedLocation, actualLocation);
            }
        }

        [Theory]
        [InlineData("node10")]
        [InlineData("node14")]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNewNodeForNewNodeHandlerHostContextVarUnset(string nodeVersion)
        {
            using (TestHostContext thc = CreateTestHostContext())
            {
                thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                var variables = new Dictionary<string, VariableValue>();

                // Explicitly set variable feature flag to false
                variables.Add(nodeVersion == "node14" ? "AGENT_USE_NODE14" : "AGENT_USE_NODE10", new VariableValue("false"));

                NodeHandler nodeHandler = new NodeHandler();

                nodeHandler.Initialize(thc);
                nodeHandler.ExecutionContext = CreateTestExecutionContext(thc, variables);
                nodeHandler.Data = nodeVersion == "node14" ? (BaseNodeHandlerData) new Node14HandlerData() :  (BaseNodeHandlerData) new Node10HandlerData();

                // Node version handler is unaffected by the variable feature flag, so folder name should be 'node10 or node14'
                string actualLocation = nodeHandler.GetNodeLocation();
                string expectedLocation = Path.Combine(thc.GetDirectory(WellKnownDirectory.Externals),
                    nodeVersion,
                    "bin",
                    $"node{IOUtil.ExeExtension}");
                Assert.Equal(expectedLocation, actualLocation);
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
    }
}