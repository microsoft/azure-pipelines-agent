// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Microsoft.TeamFoundation.Framework.Common;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(AgentPluginManager))]
    public interface IAgentPluginManager : IAgentService
    {
        List<string> GetTaskPlugins(Guid taskId);
        AgentCommandPluginInfo GetPluginCommad(string commandArea, string commandEvent);
        Task RunPluginTaskAsync(IExecutionContext context, string plugin, Dictionary<string, string> inputs, Dictionary<string, string> environment, Variables runtimeVariables, EventHandler<ProcessDataReceivedEventArgs> outputHandler);
        void ProcessCommand(IExecutionContext context, Command command);
    }

    public sealed class AgentPluginManager : AgentService, IAgentPluginManager
    {
        private readonly Dictionary<string, Dictionary<string, AgentCommandPluginInfo>> _supportedLoggingCommands = new Dictionary<string, Dictionary<string, AgentCommandPluginInfo>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, List<string>> _supportedTasks = new Dictionary<Guid, List<string>>();

        private readonly HashSet<string> _taskPlugins = new HashSet<string>()
        {
            "Agent.Plugins.Repository.CheckoutTask, Agent.Plugins",
            "Agent.Plugins.Repository.CleanupTask, Agent.Plugins",
            "Agent.Plugins.PipelineArtifact.DownloadPipelineArtifactTask, Agent.Plugins",
            "Agent.Plugins.PipelineArtifact.PublishPipelineArtifactTask, Agent.Plugins",
            "Agent.Plugins.PipelineArtifact.PublishPipelineArtifactTaskV1, Agent.Plugins",
            "Agent.Plugins.PipelineArtifact.DownloadPipelineArtifactTaskV1, Agent.Plugins",
            "Agent.Plugins.PipelineArtifact.DownloadPipelineArtifactTaskV1_1_0, Agent.Plugins",
            "Agent.Plugins.PipelineCache.SavePipelineCacheV0, Agent.Plugins",
            "Agent.Plugins.PipelineCache.RestorePipelineCacheV0, Agent.Plugins",
            "Agent.Plugins.PipelineArtifact.DownloadPipelineArtifactTaskV1_1_1, Agent.Plugins",
            "Agent.Plugins.PipelineArtifact.DownloadPipelineArtifactTaskV1_1_2, Agent.Plugins",
            "Agent.Plugins.PipelineArtifact.DownloadPipelineArtifactTaskV1_1_3, Agent.Plugins",
            "Agent.Plugins.PipelineArtifact.DownloadPipelineArtifactTaskV2_0_0, Agent.Plugins",
            "Agent.Plugins.PipelineArtifact.PublishPipelineArtifactTaskV0_140_0, Agent.Plugins"
        };

        private readonly HashSet<string> _commandPlugins = new HashSet<string>();

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);

            // Load task plugins
            foreach (var pluginTypeName in _taskPlugins)
            {
                IAgentTaskPlugin taskPlugin = null;
                AssemblyLoadContext.Default.Resolving += ResolveAssembly;
                try
                {
                    Trace.Info($"Load task plugin from '{pluginTypeName}'.");
                    Type type = Type.GetType(pluginTypeName, throwOnError: true);
                    taskPlugin = Activator.CreateInstance(type) as IAgentTaskPlugin;
                }
                finally
                {
                    AssemblyLoadContext.Default.Resolving -= ResolveAssembly;
                }

                ArgUtil.NotNull(taskPlugin, nameof(taskPlugin));
                ArgUtil.NotNull(taskPlugin.Id, nameof(taskPlugin.Id));
                ArgUtil.NotNullOrEmpty(taskPlugin.Stage, nameof(taskPlugin.Stage));
                if (!_supportedTasks.ContainsKey(taskPlugin.Id))
                {
                    _supportedTasks[taskPlugin.Id] = new List<string>();
                }

                Trace.Info($"Loaded task plugin id '{taskPlugin.Id}' ({taskPlugin.Stage}).");
                _supportedTasks[taskPlugin.Id].Add(pluginTypeName);
            }

            // Load command plugin
            foreach (var pluginTypeName in _commandPlugins)
            {
                IAgentCommandPlugin commandPlugin = null;
                AssemblyLoadContext.Default.Resolving += ResolveAssembly;
                try
                {
                    Trace.Info($"Load command plugin from '{pluginTypeName}'.");
                    Type type = Type.GetType(pluginTypeName, throwOnError: true);
                    commandPlugin = Activator.CreateInstance(type) as IAgentCommandPlugin;
                }
                finally
                {
                    AssemblyLoadContext.Default.Resolving -= ResolveAssembly;
                }

                ArgUtil.NotNull(commandPlugin, nameof(commandPlugin));
                ArgUtil.NotNullOrEmpty(commandPlugin.Area, nameof(commandPlugin.Area));
                ArgUtil.NotNullOrEmpty(commandPlugin.Event, nameof(commandPlugin.Event));
                ArgUtil.NotNullOrEmpty(commandPlugin.DisplayName, nameof(commandPlugin.DisplayName));

                if (!_supportedLoggingCommands.ContainsKey(commandPlugin.Area))
                {
                    _supportedLoggingCommands[commandPlugin.Area] = new Dictionary<string, AgentCommandPluginInfo>(StringComparer.OrdinalIgnoreCase);
                }

                Trace.Info($"Loaded command plugin to handle '##vso[{commandPlugin.Area}.{commandPlugin.Event}]'.");
                _supportedLoggingCommands[commandPlugin.Area][commandPlugin.Event] = new AgentCommandPluginInfo() { CommandPluginTypeName = pluginTypeName, DisplayName = commandPlugin.DisplayName };
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

        public AgentCommandPluginInfo GetPluginCommad(string commandArea, string commandEvent)
        {
            if (_supportedLoggingCommands.ContainsKey(commandArea) && _supportedLoggingCommands[commandArea].ContainsKey(commandEvent))
            {
                return _supportedLoggingCommands[commandArea][commandEvent];
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
                foreach (var key in inputs.Keys)
                {
                    inputs[key] = containerInfo.TranslateToHostPath(inputs[key]);
                }

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

        public void ProcessCommand(IExecutionContext context, Command command)
        {
            // queue async command task to run agent plugin.
            context.Debug($"Process {command.Area}.{command.Event} command through agent plugin in backend.");
            if (!_supportedLoggingCommands.ContainsKey(command.Area) ||
                !_supportedLoggingCommands[command.Area].ContainsKey(command.Event))
            {
                throw new NotSupportedException(command.ToString());
            }

            var plugin = _supportedLoggingCommands[command.Area][command.Event];
            ArgUtil.NotNull(plugin, nameof(plugin));
            ArgUtil.NotNullOrEmpty(plugin.DisplayName, nameof(plugin.DisplayName));

            // construct plugin context
            AgentCommandPluginExecutionContext pluginContext = new AgentCommandPluginExecutionContext
            {
                Data = command.Data,
                Properties = command.Properties,
                Endpoints = context.Endpoints,
            };

            var target = context.StepTarget();
            Variables.TranslationMethod translateToHostPath = Variables.DefaultStringTranslator;

            ContainerInfo containerInfo = target as ContainerInfo;
            // Since plugins run on the host, but the inputs and variables have already been translated
            // to the container path, we need to convert them back to the host path
            // TODO: look to see if there is a better way to not have translate these back
            if (containerInfo != null)
            {
                translateToHostPath = (string val) => { return containerInfo.TranslateToHostPath(val); };
            }
            // variables
            context.Variables.CopyInto(pluginContext.Variables, translateToHostPath);

            var commandContext = HostContext.CreateService<IAsyncCommandContext>();
            commandContext.InitializeCommandContext(context, plugin.DisplayName);
            commandContext.Task = ProcessPluginCommandAsync(commandContext, pluginContext, plugin.CommandPluginTypeName, command, context.CancellationToken);
            context.AsyncCommands.Add(commandContext);
        }

        private async Task ProcessPluginCommandAsync(IAsyncCommandContext context, AgentCommandPluginExecutionContext pluginContext, string plugin, Command command, CancellationToken token)
        {
            // Resolve the working directory.
            string workingDirectory = HostContext.GetDirectory(WellKnownDirectory.Work);
            ArgUtil.Directory(workingDirectory, nameof(workingDirectory));

            // Agent.PluginHost
            string file = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Bin), $"Agent.PluginHost{Util.IOUtil.ExeExtension}");

            // Agent.PluginHost's arguments
            string arguments = $"command \"{plugin}\"";

            // Execute the process. Exit code 0 should always be returned.
            // A non-zero exit code indicates infrastructural failure.
            // Any content coming from STDERR will indicate the command process failed.
            // We can't use ## command for plugin to communicate, since we are already processing ## command
            using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
            {
                object stderrLock = new object();
                List<string> stderr = new List<string>();
                processInvoker.OutputDataReceived += (object sender, ProcessDataReceivedEventArgs e) =>
                {
                    context.Output(e.Data);
                };
                processInvoker.ErrorDataReceived += (object sender, ProcessDataReceivedEventArgs e) =>
                {
                    lock (stderrLock)
                    {
                        stderr.Add(e.Data);
                    };
                };

                var redirectStandardIn = new InputQueue<string>();
                redirectStandardIn.Enqueue(JsonUtility.ToString(pluginContext));

                int returnCode = await processInvoker.ExecuteAsync(workingDirectory: workingDirectory,
                                                                   fileName: file,
                                                                   arguments: arguments,
                                                                   environment: null,
                                                                   requireExitCodeZero: false,
                                                                   outputEncoding: null,
                                                                   killProcessOnCancel: false,
                                                                   redirectStandardIn: redirectStandardIn,
                                                                   cancellationToken: token);

                if (returnCode != 0)
                {
                    context.Output(string.Join(Environment.NewLine, stderr));
                    throw new ProcessExitCodeException(returnCode, file, arguments);
                }
                else if (stderr.Count > 0)
                {
                    throw new InvalidOperationException(string.Join(Environment.NewLine, stderr));
                }
                else
                {
                    // Everything works fine.
                    // Return code is 0.
                    // No STDERR comes out.
                }
            }
        }

        private Assembly ResolveAssembly(AssemblyLoadContext context, AssemblyName assembly)
        {
            string assemblyFilename = assembly.Name + ".dll";
            return context.LoadFromAssemblyPath(Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Bin), assemblyFilename));
        }
    }

    public class AgentCommandPluginInfo
    {
        public string CommandPluginTypeName { get; set; }
        public string DisplayName { get; set; }
    }
}
