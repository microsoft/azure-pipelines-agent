// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Build;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class PipelineArtifactCommandExtensionL0
    {
        private const string PublishPluginTypeName = "Agent.Plugins.PipelineArtifact.PublishPipelineArtifactTaskV1, Agent.Plugins";

        private Mock<IExecutionContext> _ec;
        private Mock<IAgentPluginManager> _pluginManager;
        private Mock<IWorkerCommandManager> _commandManager;
        private Variables _variables;
        private List<IAsyncCommandContext> _asyncCommands;

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void RegistersPipelineArtifactAreaAndPublishCommand()
        {
            var extension = new PipelineArtifactCommandExtension();

            Assert.Equal("pipelineartifact", extension.CommandArea);
            Assert.Equal(HostTypes.Build, extension.SupportedHostTypes);
            Assert.Equal("publish", new PublishPipelineArtifactCommand().Name);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void ThrowsWhenPathIsMissing()
        {
            using (var hc = SetupMocks())
            {
                var command = new PublishPipelineArtifactCommand();
                var cmd = new Command("pipelineartifact", "publish");
                cmd.Properties.Add("artifactname", "drop");
                // No Data (path) supplied.

                Assert.Throws<Exception>(() => command.Execute(_ec.Object, cmd));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DelegatesToPublishPluginWithInputs()
        {
            using (var hc = SetupMocks())
            {
                string capturedPlugin = null;
                Dictionary<string, string> capturedInputs = null;
                _pluginManager
                    .Setup(x => x.RunPluginTaskAsync(
                        It.IsAny<IExecutionContext>(),
                        It.IsAny<string>(),
                        It.IsAny<Dictionary<string, string>>(),
                        It.IsAny<Dictionary<string, string>>(),
                        It.IsAny<Variables>(),
                        It.IsAny<EventHandler<ProcessDataReceivedEventArgs>>()))
                    .Returns(Task.CompletedTask)
                    .Callback<IExecutionContext, string, Dictionary<string, string>, Dictionary<string, string>, Variables, EventHandler<ProcessDataReceivedEventArgs>>(
                        (ctx, plugin, inputs, env, vars, handler) =>
                        {
                            capturedPlugin = plugin;
                            capturedInputs = inputs;
                        });

                var command = new PublishPipelineArtifactCommand();
                var cmd = new Command("pipelineartifact", "publish");
                cmd.Data = "/work/1/a/drop";
                cmd.Properties.Add("artifactname", "drop");
                cmd.Properties.Add("properties", "{\"custom.key\":\"value\"}");

                command.Execute(_ec.Object, cmd);

                // Execute queues the publish as an async command; run it to completion.
                Assert.Single(_asyncCommands);
                await _asyncCommands[0].Task;

                Assert.Equal(PublishPluginTypeName, capturedPlugin);
                Assert.NotNull(capturedInputs);
                Assert.Equal("/work/1/a/drop", capturedInputs["path"]);
                Assert.Equal("drop", capturedInputs["artifactName"]);
                Assert.Equal("{\"custom.key\":\"value\"}", capturedInputs["properties"]);

                _commandManager.Verify(x => x.EnablePluginInternalCommand(true), Times.Once);
                _commandManager.Verify(x => x.EnablePluginInternalCommand(false), Times.Once);
            }
        }

        private TestHostContext SetupMocks([CallerMemberName] string name = "")
        {
            var hc = new TestHostContext(this, name);

            _pluginManager = new Mock<IAgentPluginManager>();
            _commandManager = new Mock<IWorkerCommandManager>();
            hc.SetSingleton(_pluginManager.Object);
            hc.SetSingleton(_commandManager.Object);

            // The publish command creates an IAsyncCommandContext via CreateService.
            var asyncCommandContext = new Mock<IAsyncCommandContext>();
            asyncCommandContext.SetupProperty(x => x.Task);
            hc.EnqueueInstance(asyncCommandContext.Object);

            var variableDictionary = new Dictionary<string, VariableValue>
            {
                { "system.teamProjectId", "11111111-1111-1111-1111-111111111111" },
                { "build.buildId", "123" },
            };
            _variables = new Variables(hc, variableDictionary, out _);

            _asyncCommands = new List<IAsyncCommandContext>();

            _ec = new Mock<IExecutionContext>();
            _ec.Setup(x => x.Endpoints).Returns(new List<ServiceEndpoint> { new ServiceEndpoint() });
            _ec.Setup(x => x.Variables).Returns(_variables);
            _ec.Setup(x => x.AsyncCommands).Returns(_asyncCommands);
            _ec.Setup(x => x.GetHostContext()).Returns(hc);

            return hc;
        }
    }
}
