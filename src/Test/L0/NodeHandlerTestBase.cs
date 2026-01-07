// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
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
                    ResetEnvironment();
                }
                disposed = true;
            }
        }

        protected void RunScenarioAndAssert(TestScenario scenario, bool useStrategy)
        {
            ResetEnvironment();
            
            foreach (var knob in scenario.Knobs)
            {
                Environment.SetEnvironmentVariable(knob.Key, knob.Value);
            }
            
            Environment.SetEnvironmentVariable("AGENT_USE_NODE_STRATEGY", useStrategy ? "true" : "false");

            try
            {
                using (TestHostContext thc = new TestHostContext(this, scenario.Name))
                {
                    thc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
                    thc.SetSingleton(new ExtensionManager() as IExtensionManager);

                    var glibcCheckerMock = SetupMockedGlibcCompatibilityInfoProvider(scenario);
                    thc.SetSingleton<IGlibcCompatibilityInfoProvider>(glibcCheckerMock.Object);

                    ConfigureNodeHandlerHelper(scenario);

                    NodeHandler nodeHandler = new NodeHandler(NodeHandlerHelper.Object);
                    nodeHandler.Initialize(thc);

                    var executionContextMock = CreateTestExecutionContext(thc, scenario);
                    nodeHandler.ExecutionContext = executionContextMock.Object;
                    nodeHandler.Data = CreateHandlerData(scenario.HandlerDataType);

                    var expectations = GetScenarioExpectations(scenario, useStrategy);
                    
                    try{

                        string actualLocation = nodeHandler.GetNodeLocation(
                            node20ResultsInGlibCError: scenario.Node20GlibcError,
                            node24ResultsInGlibCError: scenario.Node24GlibcError,
                            inContainer: scenario.InContainer);

                        string expectedLocation = GetExpectedNodeLocation(expectations.ExpectedNode, scenario, thc);
                        Assert.Equal(expectedLocation, actualLocation);
                    }
                    catch (Exception ex)
                    {
                        Assert.NotNull(ex);
                        Assert.IsType(scenario.ExpectedErrorType, ex);

                        if (!string.IsNullOrEmpty(expectations.ExpectedError))
                        {
                            Assert.Contains(expectations.ExpectedError, ex.Message);
                        }
                    }
                }
            }
            finally
            {
                ResetEnvironment();
            }
        }

        /// <summary>
        /// Sets up a mocked GlibcCompatibilityInfoProvider for focused NodeHandler testing.
        /// </summary>
        private Mock<IGlibcCompatibilityInfoProvider> SetupMockedGlibcCompatibilityInfoProvider(TestScenario scenario)
        {
            var glibcCheckerMock = new Mock<IGlibcCompatibilityInfoProvider>();
            
            var glibcInfo = GlibcCompatibilityInfo.Create(
                scenario.Node24GlibcError,
                scenario.Node20GlibcError);
            
            glibcCheckerMock
                .Setup(x => x.CheckGlibcCompatibilityAsync())
                .ReturnsAsync(glibcInfo);
            
            glibcCheckerMock
                .Setup(x => x.GetGlibcCompatibilityAsync(It.IsAny<TaskContext>()))
                .ReturnsAsync(glibcInfo);
            
            return glibcCheckerMock;
        }

        private void ConfigureNodeHandlerHelper(TestScenario scenario)
        {
            NodeHandlerHelper.Reset();

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

        private string GetExpectedNodeLocation(string expectedNode, TestScenario scenario, TestHostContext thc)
        {
            if (!string.IsNullOrWhiteSpace(scenario.CustomNodePath))
            {
                return scenario.CustomNodePath;
            }
            
            return Path.Combine(
                thc.GetDirectory(WellKnownDirectory.Externals),
                expectedNode,
                "bin",
                $"node{IOUtil.ExeExtension}");
        }

        protected ScenarioExpectations GetScenarioExpectations(TestScenario scenario, bool useStrategy)
        {
            // Check if this is an equivalent scenario by seeing if strategy-specific fields are populated
            bool isEquivalentScenario = string.IsNullOrEmpty(scenario.StrategyExpectedNode) && 
                                       string.IsNullOrEmpty(scenario.LegacyExpectedNode);
            
            if (isEquivalentScenario)
            {
                // Equivalent scenarios: same behavior for both modes, use shared ExpectedNode
                return new ScenarioExpectations
                {
                    ExpectedNode = scenario.ExpectedNode,
                    ExpectedError = null
                };
            }
            else
            {
                // Divergent scenarios: different behavior between legacy and strategy
                if (useStrategy)
                {
                    return new ScenarioExpectations
                    {
                        ExpectedNode = scenario.StrategyExpectedNode,
                        ExpectedError = scenario.StrategyExpectedError
                    };
                }
                else
                {
                    return new ScenarioExpectations
                    {
                        ExpectedNode = scenario.LegacyExpectedNode,
                        ExpectedError = null
                    };
                }
            }
        }

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
                    if (variables.TryGetValue(variableName, out VariableValue value))
                    {
                        return value.Value;
                    }
                    return Environment.GetEnvironmentVariable(variableName);
                });

            return executionContext;
        }

        protected Mock<IExecutionContext> CreateTestExecutionContext(TestHostContext tc, TestScenario scenario)
        {
            var executionContext = CreateTestExecutionContext(tc, scenario.Knobs);
            
            if (!string.IsNullOrWhiteSpace(scenario.CustomNodePath))
            {
                var stepTarget = CreateStepTargetObject(scenario);
                executionContext
                    .Setup(x => x.StepTarget())
                    .Returns(stepTarget);
            }
            else
            {
                executionContext
                    .Setup(x => x.StepTarget())
                    .Returns((ExecutionTargetInfo)null);
            }
            
            return executionContext;
        }

        private ExecutionTargetInfo CreateStepTargetObject(TestScenario scenario)
        {
            if (scenario.InContainer)
            {
                return new ContainerInfo()
                {
                    CustomNodePath = scenario.CustomNodePath
                };
            }
            else
            {
                return new HostInfo()
                {
                    CustomNodePath = scenario.CustomNodePath
                };
            }
        }

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

        protected void ResetEnvironment()
        { 
            // Core Node.js strategy knobs
            Environment.SetEnvironmentVariable("AGENT_USE_NODE10", null);
            Environment.SetEnvironmentVariable("AGENT_USE_NODE20_1", null);
            Environment.SetEnvironmentVariable("AGENT_USE_NODE24", null);
            Environment.SetEnvironmentVariable("AGENT_USE_NODE24_WITH_HANDLER_DATA", null);
            Environment.SetEnvironmentVariable("AGENT_USE_NODE", null);
            
            // EOL and strategy control
            Environment.SetEnvironmentVariable("AGENT_RESTRICT_EOL_NODE_VERSIONS", null);
            Environment.SetEnvironmentVariable("AGENT_USE_NODE_STRATEGY", null);
            
            // System-specific knobs
            Environment.SetEnvironmentVariable("AGENT_USE_NODE20_IN_UNSUPPORTED_SYSTEM", null);
            Environment.SetEnvironmentVariable("AGENT_USE_NODE24_IN_UNSUPPORTED_SYSTEM", null);            
            
        }
    }

    public class TestResult
    {
        public string NodePath { get; set; }
        public Exception Exception { get; set; }
    }

    public class ScenarioExpectations
    {
        public string ExpectedNode { get; set; }
        public string ExpectedError { get; set; }
    }
}