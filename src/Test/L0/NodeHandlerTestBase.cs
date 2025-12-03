// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Moq;
using Xunit;
using Agent.Sdk;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    /// <summary>
    /// Simplified base class for NodeHandler test execution.
    /// Sets up test environment, runs tests, and cleans up.
    /// </summary>
    public abstract class NodeHandlerTestBase : IDisposable
    {
        protected Mock<INodeHandlerHelper> NodeHandlerHelper { get; private set; }
        private bool disposed = false;

        protected NodeHandlerTestBase()
        {
            NodeHandlerHelper = GetMockedNodeHandlerHelper();
            ResetEnvironment();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Clean up managed resources
                    ResetEnvironment();
                }
                disposed = true;
            }
        }

        /// <summary>
        /// Execute a test scenario and assert the expected behavior.
        /// </summary>
        protected void RunScenarioAndAssert(TestScenario scenario)
        {
            // Reset environment before each test
            ResetEnvironment();
            
            // Set up environment variables from scenario
            foreach (var knob in scenario.Knobs)
            {
                Environment.SetEnvironmentVariable(knob.Key, knob.Value);
            }
            
            // Always use unified strategy for new tests
            Environment.SetEnvironmentVariable("AGENT_USE_UNIFIED_NODE_STRATEGY", "true");

            try
            {
                using (TestHostContext thc = new TestHostContext(this, scenario.Name))
                {
                    thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                    thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                    // Setup node handler with mocks
                    ConfigureNodeHandlerHelper(scenario);

                    NodeHandler nodeHandler = new NodeHandler(NodeHandlerHelper.Object);
                    nodeHandler.Initialize(thc);

                    // Setup execution context
                    var executionContextMock = CreateTestExecutionContext(thc, scenario.Knobs);
                    nodeHandler.ExecutionContext = executionContextMock.Object;
                    nodeHandler.Data = CreateHandlerData(scenario.HandlerDataType);

                    // Execute test
                    if (scenario.ExpectSuccess)
                    {
                        string actualLocation = nodeHandler.GetNodeLocation(
                            node20ResultsInGlibCError: scenario.Node20GlibcError,
                            node24ResultsInGlibCError: scenario.Node24GlibcError,
                            inContainer: scenario.InContainer);

                        string expectedLocation = GetExpectedNodeLocation(scenario, thc);
                        Assert.Equal(expectedLocation, actualLocation);
                    }
                    else
                    {
                        var exception = Assert.Throws(scenario.ExpectedErrorType ?? typeof(FileNotFoundException),
                            () => nodeHandler.GetNodeLocation(
                                node20ResultsInGlibCError: scenario.Node20GlibcError,
                                node24ResultsInGlibCError: scenario.Node24GlibcError,
                                inContainer: scenario.InContainer));

                        // Verify error message if specified
                        if (!string.IsNullOrEmpty(scenario.UnifiedExpectedError))
                        {
                            Assert.Contains(scenario.UnifiedExpectedError, exception.Message);
                        }
                    }
                }
            }
            finally
            {
                // Always clean up after test
                ResetEnvironment();
            }
        }

        /// <summary>
        /// Configure NodeHandlerHelper mock based on scenario conditions.
        /// </summary>
        private void ConfigureNodeHandlerHelper(TestScenario scenario)
        {
            NodeHandlerHelper.Reset();

            // Simple setup: all nodes are available unless specifically disabled
            NodeHandlerHelper
                .Setup(x => x.IsNodeFolderExist(It.IsAny<string>(), It.IsAny<IHostContext>()))
                .Returns(true);

            NodeHandlerHelper
                .Setup(x => x.GetNodeFolderPath(It.IsAny<string>(), It.IsAny<IHostContext>()))
                .Returns((string nodeFolderName, IHostContext hostContext) => Path.Combine(
                    hostContext.GetDirectory(WellKnownDirectory.Externals),
                    nodeFolderName,
                    "bin",
                    $"node{IOUtil.ExeExtension}"));
        }

        /// <summary>
        /// Get expected node location based on scenario.
        /// </summary>
        private string GetExpectedNodeLocation(TestScenario scenario, TestHostContext thc)
        {
            string expectedNode = scenario.ExpectedNode;
            return Path.Combine(
                thc.GetDirectory(WellKnownDirectory.Externals),
                expectedNode,
                "bin",
                $"node{IOUtil.ExeExtension}");
        }

        /// <summary>
        /// Create handler data instance based on type.
        /// </summary>
        private BaseNodeHandlerData CreateHandlerData(Type handlerDataType)
        {
            if (handlerDataType == typeof(NodeHandlerData))
                return new NodeHandlerData();
            else if (handlerDataType == typeof(Node10HandlerData))
                return new Node10HandlerData();
            else if (handlerDataType == typeof(Node16HandlerData))
                return new Node16HandlerData();
            else if (handlerDataType == typeof(Node20_1HandlerData))
                return new Node20_1HandlerData();
            else if (handlerDataType == typeof(Node24HandlerData))
                return new Node24HandlerData();
            else
                throw new ArgumentException($"Unknown handler data type: {handlerDataType}");
        }

        /// <summary>
        /// Create test execution context with environment variables.
        /// </summary>
        private Mock<IExecutionContext> CreateTestExecutionContext(TestHostContext tc, Dictionary<string, string> knobs)
        {
            var executionContext = new Mock<IExecutionContext>();
            var variables = new Dictionary<string, VariableValue>();
            
            foreach (var knob in knobs)
            {
                variables[knob.Key] = new VariableValue(knob.Value);
            }

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
                    // Check variables first, then environment
                    if (variables.TryGetValue(variableName, out VariableValue value))
                    {
                        return value.Value;
                    }
                    return Environment.GetEnvironmentVariable(variableName);
                });

            return executionContext;
        }

        /// <summary>
        /// Get mocked node handler helper with default behavior.
        /// </summary>
        private Mock<INodeHandlerHelper> GetMockedNodeHandlerHelper()
        {
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

            return nodeHandlerHelper;
        }

        /// <summary>
        /// Reset all node-related environment variables.
        /// </summary>
        private void ResetEnvironment()
        {
            // Core Node.js strategy knobs
            Environment.SetEnvironmentVariable("AGENT_USE_NODE10", null);
            Environment.SetEnvironmentVariable("AGENT_USE_NODE20_1", null);
            Environment.SetEnvironmentVariable("AGENT_USE_NODE24", null);
            Environment.SetEnvironmentVariable("AGENT_USE_NODE24_WITH_HANDLER_DATA", null);
            Environment.SetEnvironmentVariable("AGENT_USE_NODE", null);
            
            // EOL and strategy control
            Environment.SetEnvironmentVariable("AGENT_ENABLE_EOL_NODE_VERSION_POLICY", null);
            Environment.SetEnvironmentVariable("AGENT_USE_UNIFIED_NODE_STRATEGY", null);
            Environment.SetEnvironmentVariable("AGENT_DISABLE_NODE6_TASKS", null);
            
            // System-specific knobs
            Environment.SetEnvironmentVariable("AGENT_USE_NODE20_IN_UNSUPPORTED_SYSTEM", null);
            Environment.SetEnvironmentVariable("AGENT_USE_NODE24_IN_UNSUPPORTED_SYSTEM", null);
            
            // Agent path variables
            Environment.SetEnvironmentVariable("VSTS_AGENT_SRC_FOLDER", null);
            Environment.SetEnvironmentVariable("AGENT_TOOLSDIRECTORY", null);
            
            // Node warnings
            Environment.SetEnvironmentVariable("VSTSAGENT_ENABLE_NODE_WARNINGS", null);
            
            // Force garbage collection to ensure clean state
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}