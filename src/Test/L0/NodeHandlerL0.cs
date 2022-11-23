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
            var agentUseNode10 = Environment.GetEnvironmentVariable("AGENT_USE_NODE10");
            Environment.SetEnvironmentVariable("AGENT_USE_NODE10", null);
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
            Environment.SetEnvironmentVariable("AGENT_USE_NODE10", agentUseNode10);
        }

        [Theory]
        [InlineData("node10")]
        [InlineData("node16")]
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
                nodeHandler.Data = nodeVersion == "node16" ? (BaseNodeHandlerData)new Node16HandlerData() : (BaseNodeHandlerData)new Node10HandlerData();

                string actualLocation = nodeHandler.GetNodeLocation();
                // We should fall back to node10 for node16 tasks, since RHEL 6 is not capable with Node16.
                if (PlatformUtil.RunningOnRHEL6 && nodeVersion == "node16")
                {
                    nodeVersion = "node10";
                }
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

                    NodeHandler nodeHandler = new NodeHandler();

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

                NodeHandler nodeHandler = new NodeHandler();

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

                NodeHandler nodeHandler = new NodeHandler();

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