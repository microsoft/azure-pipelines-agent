// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk.Knob;
using Agent.Sdk.Util;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Linq;
using System.Net.Sockets;
using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(WorkerCommandManager))]
    public interface IWorkerCommandManager : IAgentService
    {
        void EnablePluginInternalCommand(bool enable);
        bool TryProcessCommand(IExecutionContext context, string input);
    }

    public sealed class WorkerCommandManager : AgentService, IWorkerCommandManager
    {
        private readonly Dictionary<string, IWorkerCommandExtension> _commandExtensions = new Dictionary<string, IWorkerCommandExtension>(StringComparer.OrdinalIgnoreCase);

        private IWorkerCommandExtension _pluginInternalCommandExtensions;

        private readonly object _commandSerializeLock = new object();

        private bool _invokePluginInternalCommand = false;

        public override void Initialize(IHostContext hostContext)
        {
            ArgUtil.NotNull(hostContext, nameof(hostContext));

            base.Initialize(hostContext);

            // Register all command extensions
            var extensionManager = hostContext.GetService<IExtensionManager>();
            foreach (var commandExt in extensionManager.GetExtensions<IWorkerCommandExtension>() ?? new List<IWorkerCommandExtension>())
            {
                Trace.Info($"Register command extension for area {commandExt.CommandArea}");
                if (!string.Equals(commandExt.CommandArea, "plugininternal", StringComparison.OrdinalIgnoreCase))
                {
                    _commandExtensions[commandExt.CommandArea] = commandExt;
                }
                else
                {
                    _pluginInternalCommandExtensions = commandExt;
                }
            }
        }

        public void EnablePluginInternalCommand(bool enable)
        {
            if (enable)
            {
                Trace.Info($"Enable plugin internal command extension.");
                _invokePluginInternalCommand = true;
            }
            else
            {
                Trace.Info($"Disable plugin internal command extension.");
                _invokePluginInternalCommand = false;
            }
        }

        public bool TryProcessCommand(IExecutionContext context, string input)
        {
            ArgUtil.NotNull(context, nameof(context));
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            // TryParse input to Command
            Command command;
            var unescapePercents = AgentKnobs.DecodePercents.GetValue(context).AsBoolean();
            if (!Command.TryParse(input, unescapePercents, out command))
            {
                // if parse fail but input contains ##vso, print warning with DOC link
                if (input.IndexOf("##vso") >= 0)
                {
                    context.Warning(StringUtil.Loc("CommandKeywordDetected", input));
                }

                return false;
            }

            IWorkerCommandExtension extension = null;
            if (_invokePluginInternalCommand && string.Equals(command.Area, _pluginInternalCommandExtensions.CommandArea, StringComparison.OrdinalIgnoreCase))
            {
                extension = _pluginInternalCommandExtensions;
            }

            if (extension != null || _commandExtensions.TryGetValue(command.Area, out extension))
            {
                if (!extension.SupportedHostTypes.HasFlag(context.Variables.System_HostType))
                {
                    context.Error(StringUtil.Loc("CommandNotSupported", command.Area, context.Variables.System_HostType));
                    context.CommandResult = TaskResult.Failed;
                    return false;
                }

                // process logging command in serialize oreder.
                lock (_commandSerializeLock)
                {
                    try
                    {
                        extension.ProcessCommand(context, command);
                    }
                    catch (SocketException ex)
                    {
                        using var vssConnection = WorkerUtilities.GetVssConnection(context);

                        ExceptionsUtil.HandleSocketException(ex, vssConnection.Uri.ToString(), context.Error);
                        context.CommandResult = TaskResult.Failed;
                    }
                    catch (Exception ex)
                    {
                        context.Error(StringUtil.Loc("CommandProcessFailed", input));
                        context.Error(ex);
                        context.CommandResult = TaskResult.Failed;
                    }
                    finally
                    {
                        // trace the ##vso command as long as the command is not a ##vso[task.debug] command.
                        if (!(string.Equals(command.Area, "task", StringComparison.OrdinalIgnoreCase) &&
                              string.Equals(command.Event, "debug", StringComparison.OrdinalIgnoreCase)))
                        {
                            context.Debug($"Processed: {input}");
                        }
                    }
                }
            }
            else
            {
                context.Warning(StringUtil.Loc("CommandNotFound", command.Area));
            }

            // Only if we've successfully parsed do we show this warning
            if (AgentKnobs.DecodePercents.GetValue(context).AsString() == "" && input.Contains("%AZP25"))
            {
                context.Warning("%AZP25 detected in ##vso command. In March 2021, the agent command parser will be updated to unescape this to %. To opt out of this behavior, set a job level variable DECODE_PERCENTS to false. Setting to true will force this behavior immediately. More information can be found at https://github.com/microsoft/azure-pipelines-agent/blob/master/docs/design/percentEncoding.md");
            }

            return true;
        }
    }

    public interface IWorkerCommandExtension : IExtension
    {
        string CommandArea { get; }

        HostTypes SupportedHostTypes { get; }

        void ProcessCommand(IExecutionContext context, Command command);
    }

    public interface IWorkerCommand
    {
        string Name { get; }

        List<string> Aliases { get; }

        void Execute(IExecutionContext context, Command command);
    }

    public abstract class BaseWorkerCommandExtension : AgentService, IWorkerCommandExtension
    {

        public string CommandArea { get; protected set; }

        public HostTypes SupportedHostTypes { get; protected set; }

        public Type ExtensionType => typeof(IWorkerCommandExtension);

        private Dictionary<string, IWorkerCommand> _commands = new Dictionary<string, IWorkerCommand>(StringComparer.OrdinalIgnoreCase);

        protected void InstallWorkerCommand(IWorkerCommand commandExecutor)
        {
            ArgUtil.NotNull(commandExecutor, nameof(commandExecutor));
            if (_commands.ContainsKey(commandExecutor.Name))
            {
                throw new Exception(StringUtil.Loc("CommandDuplicateDetected", commandExecutor.Name, CommandArea.ToLowerInvariant()));
            }
            _commands[commandExecutor.Name] = commandExecutor;
            var aliasList = commandExecutor.Aliases;
            if (aliasList != null)
            {
                foreach (var alias in commandExecutor.Aliases)
                {
                    if (_commands.ContainsKey(alias))
                    {
                        throw new Exception(StringUtil.Loc("CommandDuplicateDetected", alias, CommandArea.ToLowerInvariant()));
                    }
                    _commands[alias] = commandExecutor;
                }
            }
        }

        public IWorkerCommand GetWorkerCommand(String name)
        {
            _commands.TryGetValue(name, out var commandExecutor);
            return commandExecutor;
        }

        public void ProcessCommand(IExecutionContext context, Command command)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(command, nameof(command));

            var commandExecutor = GetWorkerCommand(command.Event);
            if (commandExecutor == null)
            {
                throw new Exception(StringUtil.Loc("CommandNotFound2", CommandArea.ToLowerInvariant(), command.Event, CommandArea));
            }

            var checker = context.GetHostContext().GetService<ITaskRestrictionsChecker>();
            if (checker.CheckCommand(context, commandExecutor, command))
            {
                commandExecutor.Execute(context, command);
            }
        }
    }

    [Flags]
    public enum HostTypes
    {
        None = 0,
        Build = 1,
        Deployment = 2,
        PoolMaintenance = 4,
        Release = 8,
        All = Build | Deployment | PoolMaintenance | Release,
    }
}
