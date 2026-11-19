// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
{
    public sealed class PipelineArtifactCommandExtension : BaseWorkerCommandExtension
    {
        public PipelineArtifactCommandExtension()
        {
            CommandArea = "pipelineartifact";
            SupportedHostTypes = HostTypes.Build;
            InstallWorkerCommand(new PublishPipelineArtifactCommand());
        }
    }

    // Publishes a pipeline artifact in response to ##vso[pipelineartifact.publish].
    // The heavy lifting (dedup upload) lives in the PublishPipelineArtifact agent
    // plugin, which runs in the Agent.PluginHost process. This command is a thin
    // bridge: it packages the command properties into plugin inputs, launches the
    // plugin, and routes the plugin output back into the job log. Delegating keeps
    // the dedup client out of Agent.Worker.
    public sealed class PublishPipelineArtifactCommand : IWorkerCommand
    {
        public string Name => "publish";
        public List<string> Aliases => null;

        // Must match an entry in AgentPluginManager's supported task plugin list.
        private const string PublishPipelineArtifactPluginTypeName = "Agent.Plugins.PipelineArtifact.PublishPipelineArtifactTaskV1, Agent.Plugins";

        // Input keys expected by the PublishPipelineArtifact plugin.
        private const string PluginPathInput = "path";
        private const string PluginArtifactNameInput = "artifactName";
        private const string PluginPropertiesInput = "properties";

        public void Execute(IExecutionContext context, Command command)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(context.Endpoints, nameof(context.Endpoints));
            ArgUtil.NotNull(command, nameof(command));

            var eventProperties = command.Properties;
            var data = command.Data;

            // These are required by the plugin; validate up front for a clearer error.
            Guid projectId = context.Variables.System_TeamProjectId ?? Guid.Empty;
            ArgUtil.NotEmpty(projectId, nameof(projectId));

            int? buildId = context.Variables.Build_BuildId;
            ArgUtil.NotNull(buildId, nameof(buildId));

            if (string.IsNullOrEmpty(data))
            {
                throw new Exception(StringUtil.Loc("ArtifactLocationRequired"));
            }

            // Pass the raw (possibly container) path; AgentPluginManager translates
            // container paths back to host paths when generating the plugin context.
            var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [PluginPathInput] = data,
            };

            if (eventProperties.TryGetValue(PublishPipelineArtifactEventProperties.ArtifactName, out string artifactName) &&
                !string.IsNullOrEmpty(artifactName))
            {
                inputs[PluginArtifactNameInput] = artifactName;
            }

            if (eventProperties.TryGetValue(PublishPipelineArtifactEventProperties.Properties, out string properties) &&
                !string.IsNullOrEmpty(properties))
            {
                inputs[PluginPropertiesInput] = properties;
            }

            // Queue the publish as an async command so it runs outside the command
            // processing lock. The plugin streams its output back through the command
            // manager, which would deadlock on that lock if we ran it inline.
            var commandContext = context.GetHostContext().CreateService<IAsyncCommandContext>();
            commandContext.InitializeCommandContext(context, StringUtil.Loc("PublishPipelineArtifact"));
            commandContext.Task = PublishPipelineArtifactAsync(context, PublishPipelineArtifactPluginTypeName, inputs);
            context.AsyncCommands.Add(commandContext);
        }

        private static async Task PublishPipelineArtifactAsync(IExecutionContext context, string pluginTypeName, Dictionary<string, string> inputs)
        {
            // Ensure the remainder of this method runs on the async-command drain,
            // outside the command manager's serialize lock held by Execute's caller.
            await Task.Yield();

            var hostContext = context.GetHostContext();
            var pluginManager = hostContext.GetService<IAgentPluginManager>();
            var commandManager = hostContext.GetService<IWorkerCommandManager>();

            void OutputHandler(object sender, ProcessDataReceivedEventArgs e)
            {
                // Route the plugin's ##vso commands (task.complete, plugininternal.*, etc.)
                // through the command manager; echo everything else to the job log.
                if (!commandManager.TryProcessCommand(context, e.Data))
                {
                    context.Output(e.Data);
                }
            }

            // Allow the plugin's internal commands to be processed while it runs.
            commandManager.EnablePluginInternalCommand(true);
            try
            {
                await pluginManager.RunPluginTaskAsync(
                    context,
                    pluginTypeName,
                    inputs,
                    new Dictionary<string, string>(),
                    context.Variables,
                    OutputHandler);
            }
            finally
            {
                commandManager.EnablePluginInternalCommand(false);
            }
        }
    }

    internal static class PublishPipelineArtifactEventProperties
    {
        public static readonly string ArtifactName = "artifactname";
        public static readonly string Properties = "properties";
    }
}
