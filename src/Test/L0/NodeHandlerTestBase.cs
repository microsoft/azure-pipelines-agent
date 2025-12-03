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
            RunScenarioAndAssert(scenario, useUnifiedStrategy: true);
        }

        /// <summary>
        /// Execute a test scenario and assert the expected behavior with strategy selection.
        /// </summary>
        protected void RunScenarioAndAssert(TestScenario scenario, bool useUnifiedStrategy)
        {
            // Reset environment before each test
            ResetEnvironment();
            
            // Set up environment variables from scenario
            foreach (var knob in scenario.Knobs)
            {
                Environment.SetEnvironmentVariable(knob.Key, knob.Value);
            }
            
            // Set strategy based on parameter
            Environment.SetEnvironmentVariable("AGENT_USE_UNIFIED_NODE_STRATEGY", useUnifiedStrategy ? "true" : "false");

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

                    // Get expectations based on scenario type and strategy
                    var expectations = GetScenarioExpectations(scenario, useUnifiedStrategy);

                    // Execute test
                    if (expectations.ExpectSuccess)
                    {
                        string actualLocation = nodeHandler.GetNodeLocation(
                            node20ResultsInGlibCError: scenario.Node20GlibcError,
                            node24ResultsInGlibCError: scenario.Node24GlibcError,
                            inContainer: scenario.InContainer);

                        string expectedLocation = GetExpectedNodeLocation(expectations.ExpectedNode, thc);
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
                        if (!string.IsNullOrEmpty(expectations.ExpectedError))
                        {
                            Assert.Contains(expectations.ExpectedError, exception.Message);
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
        /// Execute a test scenario and return the result without assertions.
        /// Useful for equivalency testing where you want to compare results.
        /// </summary>
        protected TestResult RunScenarioForResult(TestScenario scenario, bool useUnifiedStrategy)
        {
            // Reset environment before each test
            ResetEnvironment();
            
            // Set up environment variables from scenario
            foreach (var knob in scenario.Knobs)
            {
                Environment.SetEnvironmentVariable(knob.Key, knob.Value);
            }
            
            // Set strategy based on parameter
            Environment.SetEnvironmentVariable("AGENT_USE_UNIFIED_NODE_STRATEGY", useUnifiedStrategy ? "true" : "false");

            try
            {
                using (TestHostContext thc = new TestHostContext(this, $"{scenario.Name}_{(useUnifiedStrategy ? "Unified" : "Legacy")}"))
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
                    try
                    {
                        string actualLocation = nodeHandler.GetNodeLocation(
                            node20ResultsInGlibCError: scenario.Node20GlibcError,
                            node24ResultsInGlibCError: scenario.Node24GlibcError,
                            inContainer: scenario.InContainer);

                        return new TestResult 
                        { 
                            Success = true, 
                            NodePath = actualLocation 
                        };
                    }
                    catch (Exception ex)
                    {
                        return new TestResult 
                        { 
                            Success = false, 
                            Exception = ex 
                        };
                    }
                }
            }
            finally
            {
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
        /// Get expected node location based on node folder name.
        /// </summary>
        private string GetExpectedNodeLocation(string expectedNode, TestHostContext thc)
        {
            return Path.Combine(
                thc.GetDirectory(WellKnownDirectory.Externals),
                expectedNode,
                "bin",
                $"node{IOUtil.ExeExtension}");
        }

        /// <summary>
        /// Get scenario expectations based on whether it's equivalent or divergent and which strategy is being used.
        /// </summary>
        protected ScenarioExpectations GetScenarioExpectations(TestScenario scenario, bool useUnifiedStrategy)
        {
            if (scenario.ShouldMatchBetweenModes)
            {
                // Equivalent scenarios: use the common expectedNode and expectSuccess
                return new ScenarioExpectations
                {
                    ExpectedNode = scenario.ExpectedNode,
                    ExpectSuccess = scenario.ExpectSuccess,
                    ExpectedError = scenario.UnifiedExpectedError // May be null, that's fine
                };
            }
            else
            {
                // Divergent scenarios: use strategy-specific expectations
                if (useUnifiedStrategy)
                {
                    return new ScenarioExpectations
                    {
                        ExpectedNode = scenario.UnifiedExpectedNode,
                        ExpectSuccess = scenario.UnifiedExpectSuccess,
                        ExpectedError = scenario.UnifiedExpectedError
                    };
                }
                else
                {
                    return new ScenarioExpectations
                    {
                        ExpectedNode = scenario.LegacyExpectedNode,
                        ExpectSuccess = scenario.LegacyExpectSuccess,
                        ExpectedError = null // Legacy doesn't have expected errors typically
                    };
                }
            }
        }

        /// <summary>
        /// Create handler data instance based on type.
        /// </summary>
        protected BaseNodeHandlerData CreateHandlerData(Type handlerDataType)
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
        protected Mock<IExecutionContext> CreateTestExecutionContext(TestHostContext tc, Dictionary<string, string> knobs)
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
        protected void ResetEnvironment()
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

    /// <summary>
    /// Result of running a test scenario.
    /// </summary>
    public class TestResult
    {
        public bool Success { get; set; }
        public string NodePath { get; set; }
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// Encapsulates expectations for a scenario based on strategy.
    /// </summary>
    public class ScenarioExpectations
    {
        public string ExpectedNode { get; set; }
        public bool ExpectSuccess { get; set; }
        public string ExpectedError { get; set; }
    }
}