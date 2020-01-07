// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Text;
using Microsoft.TeamFoundation.Framework.Common;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(AgentPluginManager))]
    public interface IAgentPluginManager : IAgentService
    {
        List<string> GetTaskPlugins(Guid taskId);
        Task RunPluginTaskAsync(IExecutionContext context, string plugin, Dictionary<string, string> inputs, Dictionary<string, string> environment, Variables runtimeVariables, EventHandler<ProcessDataReceivedEventArgs> outputHandler);
    }

    public sealed class AgentPluginManager : AgentService, IAgentPluginManager
    {
        private readonly Dictionary<Guid, List<string>> _supportedTasks = new Dictionary<Guid, List<string>>();

        private HashSet<string> _taskPlugins = new HashSet<string>();

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.FullName.StartsWith("Agent.Plugins", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (typeof(IAgentTaskPlugin).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
                        {
                            IAgentTaskPlugin taskPlugin = Activator.CreateInstance(type) as IAgentTaskPlugin;
                            ArgUtil.NotNull(taskPlugin, nameof(taskPlugin));
                            ArgUtil.NotNull(taskPlugin.Id, nameof(taskPlugin.Id));
                            ArgUtil.NotNullOrEmpty(taskPlugin.Stage, nameof(taskPlugin.Stage));
                            if (!_supportedTasks.ContainsKey(taskPlugin.Id))
                            {
                                _supportedTasks[taskPlugin.Id] = new List<string>();
                            }

                            Trace.Info($"Loaded task plugin id '{taskPlugin.Id}' ({taskPlugin.Stage}).");
                            var pluginTypeName = $"{type.FullName}, {assembly.FullName.Split(',')[0]}";
                            _taskPlugins.Add(pluginTypeName);
                            _supportedTasks[taskPlugin.Id].Add(pluginTypeName);
                        }
                    }
                }
            }
        }

        public List<string> GetTaskPlugins(Guid taskId)
        {
            if (_supportedTasks.ContainsKey(taskId))
            {
                return _supportedTasks[taskId];
            }
            else
            {
                return null;
            }
        }

        public async Task RunPluginTaskAsync(IExecutionContext context, string plugin, Dictionary<string, string> inputs, Dictionary<string, string> environment, Variables runtimeVariables, EventHandler<ProcessDataReceivedEventArgs> outputHandler)
        {
            ArgUtil.NotNullOrEmpty(plugin, nameof(plugin));

            // Only allow plugins we defined
            if (!_taskPlugins.Contains(plugin))
            {
                throw new NotSupportedException(plugin);
            }

            // Resolve the working directory.
            string workingDirectory = HostContext.GetDirectory(WellKnownDirectory.Work);
            ArgUtil.Directory(workingDirectory, nameof(workingDirectory));

            // Agent.PluginHost
            string file = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Bin), $"Agent.PluginHost{Util.IOUtil.ExeExtension}");
            ArgUtil.File(file, $"Agent.PluginHost{Util.IOUtil.ExeExtension}");

            // Agent.PluginHost's arguments
            string arguments = $"task \"{plugin}\"";

            // construct plugin context
            var target = context.StepTarget();
            Variables.TranslationMethod translateToHostPath = Variables.DefaultStringTranslator;

            ContainerInfo containerInfo = target as ContainerInfo;
            // Since plugins run on the host, but the inputs and variables have already been translated
            // to the container path, we need to convert them back to the host path
            // TODO: look to see if there is a better way to not have translate these back
            if (containerInfo != null)
            {
                var newInputs = new Dictionary<string,string>();
                foreach (var entry in inputs)
                {
                    newInputs[entry.Key] = containerInfo.TranslateToHostPath(entry.Value);
                }
                inputs = newInputs;
                translateToHostPath = (string val) => { return containerInfo.TranslateToHostPath(val); };
            }

            AgentTaskPluginExecutionContext pluginContext = new AgentTaskPluginExecutionContext
            {
                Inputs = inputs,
                Repositories = context.Repositories,
                Endpoints = context.Endpoints,
                Container = containerInfo, //TODO: Figure out if this needs to have all the containers or just the one for the current step
                JobSettings = context.JobSettings,
            };

            // variables
            runtimeVariables.CopyInto(pluginContext.Variables, translateToHostPath);
            context.TaskVariables.CopyInto(pluginContext.TaskVariables, translateToHostPath);

            using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
            {
                var redirectStandardIn = new InputQueue<string>();
                redirectStandardIn.Enqueue(JsonUtility.ToString(pluginContext));

                processInvoker.OutputDataReceived += outputHandler;
                processInvoker.ErrorDataReceived += outputHandler;

                // Execute the process. Exit code 0 should always be returned.
                // A non-zero exit code indicates infrastructural failure.
                // Task failure should be communicated over STDOUT using ## commands.
                await processInvoker.ExecuteAsync(workingDirectory: workingDirectory,
                                                  fileName: file,
                                                  arguments: arguments,
                                                  environment: environment,
                                                  requireExitCodeZero: true,
                                                  outputEncoding: Encoding.UTF8,
                                                  killProcessOnCancel: false,
                                                  redirectStandardIn: redirectStandardIn,
                                                  cancellationToken: context.CancellationToken);
            }
        }
    }
}
