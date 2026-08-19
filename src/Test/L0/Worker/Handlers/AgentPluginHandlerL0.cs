// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Handlers
{
    public sealed class AgentPluginHandlerL0
    {
        private const string PluginTarget = "Test.Plugin, Test";

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public async Task FeatureDisabledRetainsComposedEnvironment()
        {
            using var hostContext = new TestHostContext(this);
            var environment = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer)
            {
                ["HTTP_PROXY"] = "job",
                ["SHARED_VALUE"] = "explicit",
            };
            var runtimeVariables = new Variables(
                hostContext,
                new Dictionary<string, VariableValue>
                {
                    ["HTTP_PROXY"] = string.Empty,
                    ["shared.value"] = "runtime",
                    ["runtime.secret"] = new VariableValue("secret", isSecret: true),
                },
                out _);

            (Dictionary<string, string> launchedEnvironment, Variables launchedRuntimeVariables) = await RunHandlerAsync(
                hostContext,
                useJobScopedTaskEnvironment: false,
                environment,
                runtimeVariables);

            Assert.Same(environment, launchedEnvironment);
            Assert.Same(runtimeVariables, launchedRuntimeVariables);
            Assert.Equal("job", launchedEnvironment["HTTP_PROXY"]);
            Assert.Equal("explicit", launchedEnvironment["SHARED_VALUE"]);
            Assert.False(launchedEnvironment.ContainsKey("RUNTIME_SECRET"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public async Task FeatureEnabledProjectsPublicRuntimeVariablesWithHighestPrecedence()
        {
            using var hostContext = new TestHostContext(this);
            var environment = new TaskEnvironment(new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer)
            {
                ["SHARED_VALUE"] = "explicit",
            });
            var state = new TaskEnvironmentState();
            state.Set("HTTP_PROXY", "job");
            state.Remove("RESTORED_VALUE");
            environment.Reset(state.GetSnapshot());
            var runtimeVariables = new Variables(
                hostContext,
                new Dictionary<string, VariableValue>
                {
                    ["HTTP_PROXY"] = string.Empty,
                    ["shared.value"] = "runtime",
                    ["restored.value"] = "restored",
                    [Constants.PathVariable] = "runtime-path",
                    ["runtime.secret"] = new VariableValue("secret", isSecret: true),
                },
                out _);

            (Dictionary<string, string> launchedEnvironment, Variables launchedRuntimeVariables) = await RunHandlerAsync(
                hostContext,
                useJobScopedTaskEnvironment: true,
                environment,
                runtimeVariables,
                prependPath: new List<string> { "prepend" });

            Assert.Same(environment, launchedEnvironment);
            Assert.Same(runtimeVariables, launchedRuntimeVariables);
            Assert.Equal(string.Empty, launchedEnvironment["HTTP_PROXY"]);
            Assert.Equal("runtime", launchedEnvironment["SHARED_VALUE"]);
            Assert.Equal("restored", launchedEnvironment["RESTORED_VALUE"]);
            Assert.Equal(
                PathUtil.PrependPath("prepend", "runtime-path"),
                launchedEnvironment[Constants.PathVariable]);
            Assert.DoesNotContain("RESTORED_VALUE", environment.RemovedEnvironmentVariables);
            Assert.False(launchedEnvironment.ContainsKey("RUNTIME_SECRET"));
            Assert.False(launchedEnvironment.ContainsKey("SECRET_RUNTIME_SECRET"));
            Assert.False(launchedEnvironment.ContainsKey("VSTS_PUBLIC_VARIABLES"));
            Assert.False(launchedEnvironment.ContainsKey("VSTS_SECRET_VARIABLES"));
        }

        private static async Task<(Dictionary<string, string> Environment, Variables RuntimeVariables)> RunHandlerAsync(
            TestHostContext hostContext,
            bool useJobScopedTaskEnvironment,
            Dictionary<string, string> environment,
            Variables runtimeVariables,
            List<string> prependPath = null)
        {
            Dictionary<string, string> capturedEnvironment = null;
            Variables capturedRuntimeVariables = null;
            var pluginManager = new Mock<IAgentPluginManager>();
            pluginManager
                .Setup(x => x.GetTaskPlugins(It.IsAny<Guid>()))
                .Returns(new List<string> { PluginTarget });
            pluginManager
                .Setup(x => x.RunPluginTaskAsync(
                    It.IsAny<IExecutionContext>(),
                    PluginTarget,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<Variables>(),
                    It.IsAny<EventHandler<ProcessDataReceivedEventArgs>>()))
                .Callback<IExecutionContext, string, Dictionary<string, string>, Dictionary<string, string>, Variables, EventHandler<ProcessDataReceivedEventArgs>>(
                    (_, _, _, pluginEnvironment, pluginRuntimeVariables, _) =>
                    {
                        capturedEnvironment = pluginEnvironment;
                        capturedRuntimeVariables = pluginRuntimeVariables;
                    })
                .Returns(Task.CompletedTask);
            hostContext.SetSingleton<IAgentPluginManager>(pluginManager.Object);
            hostContext.SetSingleton<IWorkerCommandManager>(new Mock<IWorkerCommandManager>().Object);

            var executionContext = new Mock<IExecutionContext>();
            executionContext.SetupGet(x => x.PrependPath).Returns(prependPath ?? new List<string>());
            executionContext.Setup(x => x.GetScopedEnvironment()).Returns(new LocalEnvironment());
            executionContext
                .Setup(x => x.GetVariableValueOrDefault("DistributedTask.Agent.UseJobScopedTaskEnvironment"))
                .Returns(useJobScopedTaskEnvironment.ToString());

            var handler = new AgentPluginHandler
            {
                Data = new AgentPluginHandlerData { Target = PluginTarget },
                Environment = environment,
                ExecutionContext = executionContext.Object,
                Inputs = new Dictionary<string, string>(),
                RuntimeVariables = runtimeVariables,
                StepHost = new Mock<IDefaultStepHost>().Object,
                Task = new TaskStepDefinitionReference
                {
                    Id = Guid.NewGuid(),
                    Name = "plugin test",
                    Version = "1.0.0",
                },
            };
            handler.Initialize(hostContext);

            await handler.RunAsync();

            return (capturedEnvironment, capturedRuntimeVariables);
        }
    }
}
