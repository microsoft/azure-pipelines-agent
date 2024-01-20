// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System.Threading.Tasks;
using System;
using Agent.Worker.Handlers.Helpers;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Handlers
{
    [ServiceLocator(Default = typeof(AgentPluginHandler))]
    public interface IAgentPluginHandler : IHandler
    {
        AgentPluginHandlerData Data { get; set; }
    }

    public sealed class AgentPluginHandler : Handler, IAgentPluginHandler
    {
        public AgentPluginHandlerData Data { get; set; }

        public async Task RunAsync()
        {
            // Validate args.
            Trace.Entering();
            ArgUtil.NotNull(Data, nameof(Data));
            ArgUtil.NotNull(ExecutionContext, nameof(ExecutionContext));
            ArgUtil.NotNull(Inputs, nameof(Inputs));
            ArgUtil.NotNullOrEmpty(Data.Target, nameof(Data.Target));

            // Update the env dictionary.
            AddPrependPathToEnvironment();

            // Make sure only particular task get run as agent plugin.
            var agentPlugin = HostContext.GetService<IAgentPluginManager>();
            var taskPlugins = agentPlugin.GetTaskPlugins(Task.Id);
            ArgUtil.NotNull(taskPlugins, $"{Task.Name} ({Task.Id}/{Task.Version})");
            if (!taskPlugins.Contains(Data.Target))
            {
                throw new NotSupportedException(Data.Target);
            }

            var commandManager = HostContext.GetService<IWorkerCommandManager>();
            commandManager.EnablePluginInternalCommand(true);
            try
            {
                await agentPlugin.RunPluginTaskAsync(ExecutionContext, Data.Target, Inputs, Environment, RuntimeVariables, OnDataReceived);
            }
            finally
            {
                commandManager.EnablePluginInternalCommand(false);
            }
        }

        private void OnDataReceived(object sender, ProcessDataReceivedEventArgs e)
        {
            string line = e.Data ?? string.Empty;
            if (_shouldRemoveColorsFromLogs)
            {
                line = OutputDataHelper.RemoveAnsiColorsFromLine(line);
            }

            // This does not need to be inside of a critical section.
            // The logging queues and command handlers are thread-safe.
            if (!CommandManager.TryProcessCommand(ExecutionContext, line))
            {
                ExecutionContext.Output(line);
            }
        }
    }
}
