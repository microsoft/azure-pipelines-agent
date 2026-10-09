// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk.Knob;
using Agent.Sdk.Util;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Net.Sockets;
using System.Collections.Generic;
using Agent.Sdk;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(WorkerCommandManager))]
    public interface IWorkerCommandManager : IAgentService
    {
        void EnablePluginInternalCommand(bool enable);
        void ResetCommandSuppression(IExecutionContext context);
        bool TryProcessCommand(IExecutionContext context, string input);
    }

    public sealed class WorkerCommandManager : AgentService, IWorkerCommandManager
    {
        private const string _agentCommandArea = "agent";
        private const string _pauseCommandsEvent = "pausecommands";
        private const string _resumeCommandsEvent = "resumecommands";
        private const int _minimumCommandSuppressionTokenLength = 16;
        private const int _maximumCommandSuppressionTokenLength = 128;

        private readonly Dictionary<string, IWorkerCommandExtension> _commandExtensions = new Dictionary<string, IWorkerCommandExtension>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, string> _commandSuppressionTokens = new Dictionary<Guid, string>();

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

        public void ResetCommandSuppression(IExecutionContext context)
        {
            ArgUtil.NotNull(context, nameof(context));

            lock (_commandSerializeLock)
            {
                if (_commandSuppressionTokens.Remove(context.Id))
                {
                    Trace.Info("Reset logging command suppression at task boundary.");
                    context.Output(StringUtil.Loc("LoggingCommandProcessingResumedAtTaskCompletion"));
                }
            }
        }

        public bool TryProcessCommand(IExecutionContext context, string input)
        {
            ArgUtil.NotNull(context, nameof(context));
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            lock (_commandSerializeLock)
            {
                if (_commandSuppressionTokens.TryGetValue(context.Id, out string suppressionToken))
                {
                    if (IsCommandSuppressionControlLine(input, _resumeCommandsEvent, suppressionToken))
                    {
                        _commandSuppressionTokens.Remove(context.Id);
                        Trace.Info("Resume processing logging commands.");
                        context.Output(StringUtil.Loc("LoggingCommandProcessingResumed"));
                        return true;
                    }

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

                if (IsCommandSuppressionControl(command, input, _pauseCommandsEvent))
                {
                    _commandSuppressionTokens[context.Id] = command.Data;
                    Trace.Info("Pause processing logging commands.");
                    context.Output(StringUtil.Loc("LoggingCommandProcessingPaused"));
                    return true;
                }

                if (IsCommandSuppressionEvent(command))
                {
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
                            context.Debug($"Processed: {CommandStringConvertor.Unescape(input, unescapePercents)}");
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

        private static bool IsCommandSuppressionControl(Command command, string input, string eventName)
        {
            return string.Equals(command.Area, _agentCommandArea, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(command.Event, eventName, StringComparison.OrdinalIgnoreCase) &&
                   IsValidCommandSuppressionToken(command.Data) &&
                   IsCommandSuppressionControlLine(input, eventName, command.Data);
        }

        private static bool IsCommandSuppressionEvent(Command command)
        {
            return string.Equals(command.Area, _agentCommandArea, StringComparison.OrdinalIgnoreCase) &&
                   (string.Equals(command.Event, _pauseCommandsEvent, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(command.Event, _resumeCommandsEvent, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsValidCommandSuppressionToken(string token)
        {
            if (token == null ||
                token.Length < _minimumCommandSuppressionTokenLength ||
                token.Length > _maximumCommandSuppressionTokenLength)
            {
                return false;
            }

            foreach (char value in token)
            {
                if (!char.IsAsciiLetterOrDigit(value) && value != '_' && value != '-')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsCommandSuppressionControlLine(string input, string eventName, string token)
        {
            string commandPrefix = $"##vso[{_agentCommandArea}.{eventName}]";
            return input.Length == commandPrefix.Length + token.Length &&
                   input.StartsWith(commandPrefix, StringComparison.OrdinalIgnoreCase) &&
                   string.CompareOrdinal(input, commandPrefix.Length, token, 0, token.Length) == 0;
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
