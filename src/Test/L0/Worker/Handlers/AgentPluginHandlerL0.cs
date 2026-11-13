// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Handlers
{
    public sealed class AgentPluginHandlerL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public async Task PluginOutputUsesWorkerCommandManager()
        {
            using var hostContext = new TestHostContext(this);
            var commandManager = new Mock<IWorkerCommandManager>();
            hostContext.SetSingleton(commandManager.Object);

            var executionContext = new Mock<IExecutionContext>();
            executionContext.SetupGet(x => x.PrependPath).Returns(new List<string>());

            var taskId = Guid.NewGuid();
            const string plugin = "Test.Plugin, Test";
            const string pauseCommand = "##vso[agent.pausecommands]abcdefghijklmnop";
            const string suppressedCommand = "##vso[task.setvariable variable=blocked]value";
            const string resumeCommand = "##vso[agent.resumecommands]abcdefghijklmnop";

            commandManager
                .Setup(x => x.TryProcessCommand(executionContext.Object, It.IsAny<string>()))
                .Returns((IExecutionContext _, string input) => input != suppressedCommand);

            var pluginManager = new Mock<IAgentPluginManager>();
            pluginManager
                .Setup(x => x.GetTaskPlugins(taskId))
                .Returns(new List<string> { plugin });
            pluginManager
                .Setup(x => x.RunPluginTaskAsync(
                    executionContext.Object,
                    plugin,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<Variables>(),
                    It.IsAny<EventHandler<ProcessDataReceivedEventArgs>>()))
                .Callback((
                    IExecutionContext _,
                    string _,
                    Dictionary<string, string> _,
                    Dictionary<string, string> _,
                    Variables _,
                    EventHandler<ProcessDataReceivedEventArgs> outputHandler) =>
                {
                    outputHandler(null, new ProcessDataReceivedEventArgs(pauseCommand));
                    outputHandler(null, new ProcessDataReceivedEventArgs(suppressedCommand));
                    outputHandler(null, new ProcessDataReceivedEventArgs(resumeCommand));
                })
                .Returns(Task.CompletedTask);
            hostContext.SetSingleton(pluginManager.Object);

            var handler = new AgentPluginHandler
            {
                Data = new AgentPluginHandlerData { Target = plugin },
                ExecutionContext = executionContext.Object,
                Inputs = new Dictionary<string, string>(),
                Environment = new Dictionary<string, string>(),
                RuntimeVariables = null,
                Task = new TaskStepDefinitionReference
                {
                    Id = taskId,
                    Name = "TestPlugin",
                    Version = "1.0.0"
                }
            };
            handler.Initialize(hostContext);

            await handler.RunAsync();

            commandManager.Verify(x => x.EnablePluginInternalCommand(true), Times.Once);
            commandManager.Verify(x => x.TryProcessCommand(executionContext.Object, pauseCommand), Times.Once);
            commandManager.Verify(x => x.TryProcessCommand(executionContext.Object, suppressedCommand), Times.Once);
            commandManager.Verify(x => x.TryProcessCommand(executionContext.Object, resumeCommand), Times.Once);
            commandManager.Verify(x => x.EnablePluginInternalCommand(false), Times.Once);
            executionContext.Verify(x => x.Write(null, suppressedCommand, true), Times.Once);
            executionContext.Verify(
                x => x.Write(null, It.Is<string>(line => line == pauseCommand || line == resumeCommand), true),
                Times.Never);
        }
    }
}
