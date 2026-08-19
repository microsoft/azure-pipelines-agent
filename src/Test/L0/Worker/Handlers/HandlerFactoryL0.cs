// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Moq;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Handlers
{
    public sealed class HandlerFactoryL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void FeatureDisabledUsesOriginalEnvironmentAndIgnoresJobState()
        {
            using var hostContext = new TestHostContext(this);
            var executionContext = CreateExecutionContext(useJobScopedTaskEnvironment: false);
            var state = new TaskEnvironmentState();
            state.Set("JOB", "job");
            state.Remove("EXPLICIT");
            executionContext.SetupGet(x => x.TaskEnvironmentState).Returns(state);
            var environment = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer)
            {
                ["EXPLICIT"] = "task",
            };

            IHandler handler = CreateHandler(hostContext, executionContext.Object, environment);

            Assert.Same(environment, handler.Environment);
            Assert.Equal("task", handler.Environment["EXPLICIT"]);
            Assert.False(handler.Environment.ContainsKey("JOB"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void FeatureEnabledLayersExplicitEnvironmentAboveJobState()
        {
            using var hostContext = new TestHostContext(this);
            var executionContext = CreateExecutionContext(useJobScopedTaskEnvironment: true);
            var state = new TaskEnvironmentState();
            state.Set("JOB", "job");
            state.Set("SHARED", "job");
            state.Remove("RESTORED");
            state.Remove("REMOVED");
            executionContext.SetupGet(x => x.TaskEnvironmentState).Returns(state);
            var environment = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer)
            {
                ["SHARED"] = "task",
                ["RESTORED"] = "task",
            };

            IHandler handler = CreateHandler(hostContext, executionContext.Object, environment);

            var taskEnvironment = Assert.IsType<TaskEnvironment>(handler.Environment);
            Assert.NotSame(environment, taskEnvironment);
            Assert.Equal("job", taskEnvironment["JOB"]);
            Assert.Equal("task", taskEnvironment["SHARED"]);
            Assert.Equal("task", taskEnvironment["RESTORED"]);
            Assert.False(taskEnvironment.ContainsKey("REMOVED"));
            Assert.Contains("REMOVED", taskEnvironment.RemovedEnvironmentVariables);
            Assert.DoesNotContain("RESTORED", taskEnvironment.RemovedEnvironmentVariables);
            Assert.Equal(2, environment.Count);
        }

        private static Mock<IExecutionContext> CreateExecutionContext(bool useJobScopedTaskEnvironment)
        {
            var executionContext = new Mock<IExecutionContext>();
            executionContext.Setup(x => x.GetScopedEnvironment()).Returns(new LocalEnvironment());
            executionContext
                .Setup(x => x.GetVariableValueOrDefault("DistributedTask.Agent.UseJobScopedTaskEnvironment"))
                .Returns(useJobScopedTaskEnvironment.ToString());
            return executionContext;
        }

        private static IHandler CreateHandler(
            TestHostContext hostContext,
            IExecutionContext executionContext,
            Dictionary<string, string> environment)
        {
            var handler = new Mock<INodeHandler>();
            handler.SetupAllProperties();
            hostContext.EnqueueInstance<INodeHandler>(handler.Object);
            var factory = new HandlerFactory();
            factory.Initialize(hostContext);
            var runtimeVariables = new Variables(
                hostContext,
                new Dictionary<string, VariableValue>(),
                out _);

            return factory.Create(
                executionContext,
                new TaskStepDefinitionReference(),
                new Mock<IStepHost>().Object,
                new List<ServiceEndpoint>(),
                new List<SecureFile>(),
                new Node10HandlerData(),
                new Dictionary<string, string>(),
                environment,
                runtimeVariables,
                taskDirectory: hostContext.GetDirectory(WellKnownDirectory.Temp));
        }
    }
}
