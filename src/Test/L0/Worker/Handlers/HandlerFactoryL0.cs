// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Moq;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Handlers
{
    public sealed class HandlerFactoryL0
    {
        [Theory]
        [InlineData(6, false, false, null)]
        [InlineData(6, false, false, "false")]
        [InlineData(6, false, false, "true")]
        [InlineData(6, true, true, "true")]
        [InlineData(10, false, false, "true")]
        [InlineData(16, false, false, "true")]
        [InlineData(20, false, false, "true")]
        [InlineData(24, false, false, "true")]
        [InlineData(0, false, false, "true")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void Create_WarnsForNode6RegardlessOfWarningSettings(int nodeVersion, bool useOverrides, bool exceptionListedTask, string formerDisableSetting)
        {
            using (var hostContext = new TestHostContext(this, $"{nameof(Create_WarnsForNode6RegardlessOfWarningSettings)}_{nodeVersion}_{useOverrides}"))
            {
                var variableValues = new Dictionary<string, VariableValue>
                {
                    [Constants.Variables.Task.DisplayName] = "Legacy task",
                    ["VSTSAGENT_ENABLE_NODE_WARNINGS"] = "false",
                    ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = useOverrides.ToString(),
                    ["AGENT_USE_ENHANCED_NODE_SELECTION"] = useOverrides.ToString(),
                    ["AGENT_USE_NODE24"] = useOverrides.ToString()
                };
                if (formerDisableSetting != null)
                {
                    variableValues["DISABLE_NODE6_DEPRECATION_WARNING"] = formerDisableSetting;
                }
                var variables = new Variables(hostContext, variableValues, out _);
                var executionContext = new Mock<IExecutionContext>();
                executionContext.Setup(x => x.Variables).Returns(variables);
                executionContext.Setup(x => x.GetVariableValueOrDefault(It.IsAny<string>()))
                    .Returns((string name) => variables.Get(name));

                var nodeHandler = new Mock<INodeHandler>();
                nodeHandler.SetupAllProperties();
                hostContext.EnqueueInstance(nodeHandler.Object);

                var factory = new HandlerFactory();
                factory.Initialize(hostContext);
                var handlerData = nodeVersion switch
                {
                    6 => (BaseNodeHandlerData)new NodeHandlerData(),
                    10 => new Node10HandlerData(),
                    16 => new Node16HandlerData(),
                    20 => new Node20_1HandlerData(),
                    24 => new Node24HandlerData(),
                    0 => new CustomNodeHandlerData(),
                    _ => throw new ArgumentOutOfRangeException(nameof(nodeVersion))
                };
                var task = new Pipelines.TaskStepDefinitionReference
                {
                    Id = exceptionListedTask ? new Guid("769D88CB-515B-4456-A045-D9A4E11C90E3") : Guid.NewGuid(),
                    Name = "LegacyTask",
                    Version = "1.2.3"
                };

                var handler = factory.Create(
                    executionContext.Object,
                    task,
                    Mock.Of<IStepHost>(),
                    new List<ServiceEndpoint>(),
                    new List<SecureFile>(),
                    handlerData,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>(),
                    variables,
                    "task-directory");

                Assert.Same(nodeHandler.Object, handler);
                Assert.Same(handlerData, nodeHandler.Object.Data);

                string expectedWarning = StringUtil.Loc("DeprecatedNodeRunner", "Legacy task", "1.2.3", "LegacyTask", "6");
                executionContext.Verify(x => x.AddIssue(It.Is<Issue>(issue =>
                    issue.Type == IssueType.Warning && issue.Message == expectedWarning)),
                    nodeVersion == 6 ? Times.Once() : Times.Never());
                executionContext.Verify(x => x.AddIssue(It.Is<Issue>(issue => issue.Type == IssueType.Warning)),
                    nodeVersion == 6 ? Times.Once() : Times.Never());
            }
        }
    }
}
