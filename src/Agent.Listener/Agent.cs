// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Agent.Sdk.Util;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;
using Microsoft.VisualStudio.Services.Agent.Listener.Diagnostics;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.WebApi;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.TeamFoundation.TestClient.PublishTestResults.Telemetry;
using Microsoft.VisualStudio.Services.Agent.Listener.Telemetry;
using System.Collections.Generic;
using Newtonsoft.Json;
using Agent.Sdk.Knob;
using Agent.Listener.Configuration;

namespace Microsoft.VisualStudio.Services.Agent.Listener
{
    [ServiceLocator(Default = typeof(Agent))]
    public interface IAgent : IAgentService
    {
        Task<int> ExecuteCommand(CommandSettings command);
    }

    public sealed class Agent : AgentService, IAgent, IDisposable
    {
        private IMessageListener _listener;
        private ITerminal _term;
        private bool _inConfigStage;
        private ManualResetEvent _completedCommand = new ManualResetEvent(false);

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);
            _term = HostContext.GetService<ITerminal>();
        }

        public async Task<int> ExecuteCommand(CommandSettings command)
        {
            using (Trace.EnteringWithDuration())
            {
                ArgUtil.NotNull(command, nameof(command));
                try
                {
                    Trace.Verbose("Initializing core services...");
                    var agentWebProxy = HostContext.GetService<IVstsAgentWebProxy>();
                    var agentCertManager = HostContext.GetService<IAgentCertificateManager>();
                    VssUtil.InitializeVssClientSettings(HostContext.UserAgent, agentWebProxy.WebProxy, agentCertManager.VssClientCertificateManager, agentCertManager.SkipServerCertificateValidation);

                    _inConfigStage = true;
                    _completedCommand.Reset();
                    _term.CancelKeyPress += CtrlCHandler;

                    //register a SIGTERM handler
                    HostContext.Unloading += Agent_Unloading;

                    // TODO Unit test to cover this logic
                    var configManager = HostContext.GetService<IConfigurationManager>();

                    // command is not required, if no command it just starts if configured

                    // TODO: Invalid config prints usage

                    if (command.IsHelp())
                    {
                        PrintUsage(command);
                        return Constants.Agent.ReturnCode.Success;
                    }

                    if (command.IsVersion())
                    {
                        _term.WriteLine(BuildConstants.AgentPackage.Version);
                        return Constants.Agent.ReturnCode.Success;
                    }

                    if (command.IsCommit())
                    {
                        _term.WriteLine(BuildConstants.Source.CommitHash);
                        return Constants.Agent.ReturnCode.Success;
                    }

                    if (command.IsDiagnostics())
                    {
                        PrintBanner();
                        _term.WriteLine("Running Diagnostics Only...");
                        _term.WriteLine(string.Empty);
                        DiagnosticTests diagnostics = new DiagnosticTests(_term);
                        diagnostics.Execute();
                        return Constants.Agent.ReturnCode.Success;
                    }

                    // Configure agent prompt for args if not supplied
                    // Unattend configure mode will not prompt for args if not supplied and error on any missing or invalid value.
                    if (command.IsConfigureCommand())
                    {
                        PrintBanner();
                        try
                        {
                            await configManager.ConfigureAsync(command);
                            return Constants.Agent.ReturnCode.Success;
                        }
                        catch (Exception ex)
                        {
                            Trace.Error(ex);
                            _term.WriteError(ex.Message);
                            return Constants.Agent.ReturnCode.TerminatedError;
                        }
                    }

                    // remove config files, remove service, and exit
                    if (command.IsRemoveCommand())
                    {
                        try
                        {
                            await configManager.UnconfigureAsync(command);
                            return Constants.Agent.ReturnCode.Success;
                        }
                        catch (Exception ex)
                        {
                            Trace.Error(ex);
                            _term.WriteError(ex.Message);
                            return Constants.Agent.ReturnCode.TerminatedError;
                        }
                    }

                    if (command.IsReAuthCommand())
                    {
                        try
                        {
                            await configManager.ReAuthAsync(command);
                            return Constants.Agent.ReturnCode.Success;
                        }
                        catch (Exception ex)
                        {
                            Trace.Error(ex);
                            _term.WriteError(ex.Message);
                            return Constants.Agent.ReturnCode.TerminatedError;
                        }
                    }

                    _inConfigStage = false;

                    // warmup agent process (JIT/CLR)
                    // In scenarios where the agent is single use (used and then thrown away), the system provisioning the agent can call `agent.listener --warmup` before the machine is made available to the pool for use.
                    // this will optimizes the agent process startup time.
                    if (command.IsWarmupCommand())
                    {
                        Trace.Info("Starting agent warmup process - pre-loading assemblies for optimal performance");
                        var binDir = HostContext.GetDirectory(WellKnownDirectory.Bin);
                        foreach (var assemblyFile in Directory.EnumerateFiles(binDir, "*.dll"))
                        {
                            try
                            {
                                Trace.Info($"Load assembly: {assemblyFile}.");
                                var assembly = Assembly.LoadFrom(assemblyFile);
                                var types = assembly.GetTypes();
                                foreach (Type loadedType in types)
                                {
                                    try
                                    {
                                        Trace.Info($"Load methods: {loadedType.FullName}.");
                                        var methods = loadedType.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                                        foreach (var method in methods)
                                        {
                                            if (!method.IsAbstract && !method.ContainsGenericParameters)
                                            {
                                                Trace.Verbose($"Prepare method: {method.Name}.");
                                                RuntimeHelpers.PrepareMethod(method.MethodHandle);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Trace.Error(ex);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Trace.Error(ex);
                            }
                        }

                        Trace.Info("Agent warmup completed successfully - assemblies pre-loaded");
                        return Constants.Agent.ReturnCode.Success;
                    }

                    Trace.Info("Loading agent configuration from settings store");
                    AgentSettings settings = configManager.LoadSettings();

                    var store = HostContext.GetService<IConfigurationStore>();
                    bool configuredAsService = store.IsServiceConfigured();

                    // Run agent
                    //if (command.Run) // this line is current break machine provisioner.
                    //{

                    // Error if agent not configured.
                    if (!configManager.IsConfigured())
                    {
                        Trace.Error("Agent configuration not found - agent needs to be configured before running");
                        _term.WriteError(StringUtil.Loc("AgentIsNotConfigured"));
                        PrintUsage(command);
                        return Constants.Agent.ReturnCode.TerminatedError;
                    }

                    Trace.Verbose($"Agent configuration loaded successfully, Configured as service: '{configuredAsService}'");

                    //Get the startup type of the agent i.e., autostartup, service, manual
                    StartupType startType;
                    var startupTypeAsString = command.GetStartupType();
                    if (string.IsNullOrEmpty(startupTypeAsString) && configuredAsService)
                    {
                        // We need try our best to make the startup type accurate
                        // The problem is coming from agent autoupgrade, which result an old version service host binary but a newer version agent binary
                        // At that time the servicehost won't pass --startuptype to agent.listener while the agent is actually running as service.
                        // We will guess the startup type only when the agent is configured as service and the guess will based on whether STDOUT/STDERR/STDIN been redirect or not
                        Trace.Info($"Try determine agent startup type base on console redirects.");
                        startType = (Console.IsErrorRedirected && Console.IsInputRedirected && Console.IsOutputRedirected) ? StartupType.Service : StartupType.Manual;
                    }
                    else
                    {
                        if (!Enum.TryParse(startupTypeAsString, true, out startType))
                        {
                            Trace.Info($"Could not parse the argument value '{startupTypeAsString}' for StartupType. Defaulting to {StartupType.Manual}");
                            startType = StartupType.Manual;
                        }
                    }

                    Trace.Info($"Set agent startup type - {startType}");
                    HostContext.StartupType = startType;

                    bool debugModeEnabled = command.GetDebugMode();

                    if (debugModeEnabled)
                    {
                        Trace.Warning("Agent is running in debug mode, don't use it in production");
                        settings.DebugMode = true;
                        store.SaveSettings(settings);
                    }
                    else if (settings.DebugMode && !debugModeEnabled)
                    {
                        settings.DebugMode = false;
                        store.SaveSettings(settings);
                    }

                    if (PlatformUtil.RunningOnWindows)
                    {
                        if (store.IsAutoLogonConfigured())
                        {
                            if (HostContext.StartupType != StartupType.Service)
                            {
                                Trace.Info($"Autologon is configured on the machine, dumping all the autologon related registry settings");
                                var autoLogonRegManager = HostContext.GetService<IAutoLogonRegistryManager>();
                                autoLogonRegManager.DumpAutoLogonRegistrySettings();
                            }
                            else
                            {
                                Trace.Info($"Autologon is configured on the machine but current Agent.Listener.exe is launched from the windows service");
                            }
                        }
                    }

                    //Publish inital telemetry data
                    var telemetryPublisher = HostContext.GetService<IAgenetListenerTelemetryPublisher>();

                    try
                    {
                        var systemVersion = PlatformUtil.GetSystemVersion();

                        Dictionary<string, string> telemetryData = new Dictionary<string, string>
                    {
                        { "OS", PlatformUtil.GetSystemId() ?? "" },
                        { "OSVersion", systemVersion?.Name?.ToString() ?? "" },
                        { "OSBuild", systemVersion?.Version?.ToString() ?? "" },
                        { "configuredAsService", $"{configuredAsService}"},
                        { "startupType", startupTypeAsString }
                    };
                        var cmd = new Command("telemetry", "publish");
                        cmd.Data = JsonConvert.SerializeObject(telemetryData);
                        cmd.Properties.Add("area", "PipelinesTasks");
                        cmd.Properties.Add("feature", "AgentListener");
                        await telemetryPublisher.PublishEvent(HostContext, cmd);
                    }

                    catch (Exception ex)
                    {
                        Trace.Warning($"Unable to publish telemetry data. {ex}");
                    }


                    // Run the agent interactively or as service
                    return await RunAsync(settings, command.GetRunOnce());
                }
                finally
                {
                    _term.CancelKeyPress -= CtrlCHandler;
                    HostContext.Unloading -= Agent_Unloading;
                    _completedCommand.Set();
                }
            }
        }

        public void Dispose()
        {
            _term?.Dispose();
            _completedCommand.Dispose();
        }

        private void Agent_Unloading(object sender, EventArgs e)
        {
            if ((!_inConfigStage) && (!HostContext.AgentShutdownToken.IsCancellationRequested))
            {
                HostContext.ShutdownAgent(ShutdownReason.UserCancelled);
                _completedCommand.WaitOne(Constants.Agent.ExitOnUnloadTimeout);
            }
        }

        private void CtrlCHandler(object sender, EventArgs e)
        {
            _term.WriteLine(StringUtil.Loc("Exiting"));
            if (_inConfigStage)
            {
                HostContext.Dispose();
                Environment.Exit(Constants.Agent.ReturnCode.TerminatedError);
            }
            else
            {
                ConsoleCancelEventArgs cancelEvent = e as ConsoleCancelEventArgs;
                if (cancelEvent != null && HostContext.GetService<IConfigurationStore>().IsServiceConfigured())
                {
                    ShutdownReason reason;
                    if (cancelEvent.SpecialKey == ConsoleSpecialKey.ControlBreak)
                    {
                        Trace.Info("Received Ctrl-Break signal from agent service host, this indicate the operating system is shutting down.");
                        reason = ShutdownReason.OperatingSystemShutdown;
                    }
                    else
                    {
                        Trace.Info("Received Ctrl-C signal, stop agent.listener and agent.worker.");
                        reason = ShutdownReason.UserCancelled;
                    }

                    HostContext.ShutdownAgent(reason);
                }
                else
                {
                    HostContext.ShutdownAgent(ShutdownReason.UserCancelled);
                }
            }
        }

        private async Task InitializeRuntimeFeatures()
        {
            try
            {
                Trace.Info("Initializing runtime features from feature flags");

                var featureFlagProvider = HostContext.GetService<IFeatureFlagProvider>();
                var traceManager = HostContext.GetService<ITraceManager>();

                // Check enhanced logging feature flag
                var enhancedLoggingFlag = await featureFlagProvider.GetFeatureFlagAsync(HostContext, "DistributedTask.Agent.UseEnhancedLogging", Trace);
                bool enhancedLoggingEnabled = string.Equals(enhancedLoggingFlag?.EffectiveState, "On", StringComparison.OrdinalIgnoreCase);

                Trace.Info($"Enhanced logging feature flag is {(enhancedLoggingEnabled ? "enabled" : "disabled")}");
                // Set the result on TraceManager - this automatically switches all trace sources
                traceManager.SetEnhancedLoggingEnabled(enhancedLoggingEnabled);

                // Ensure child processes (worker/plugin) pick up enhanced logging via knob
                Environment.SetEnvironmentVariable("AZP_USE_ENHANCED_LOGGING", enhancedLoggingEnabled ? "true" : null);

                Trace.Info("Runtime features initialization completed successfully");
            }
            catch (Exception ex)
            {
                // Don't fail the agent if feature flag check fails
                Trace.Warning($"Runtime features initialization failed, using defaults: {ex}");
            }
        }

        //create worker manager, create message listener and start listening to the queue
        private async Task<int> RunAsync(AgentSettings settings, bool runOnce = false)
        {
            using (Trace.EnteringWithDuration())
            {
                try
                {
                    Trace.Info(StringUtil.Format("Entering main agent execution loop({0})", nameof(RunAsync)));

                    var featureFlagProvider = HostContext.GetService<IFeatureFlagProvider>();
                    var checkPsModulesFeatureFlag = await featureFlagProvider.GetFeatureFlagAsync(HostContext, "DistributedTask.Agent.CheckPsModulesLocations", Trace);

                    if (PlatformUtil.RunningOnWindows && checkPsModulesFeatureFlag?.EffectiveState == "On")
                    {
                        string psModulePath = Environment.GetEnvironmentVariable("PSModulePath");
                        bool containsPwshLocations = PsModulePathUtil.ContainsPowershellCoreLocations(psModulePath);

                        if (containsPwshLocations)
                        {
                            _term.WriteLine(StringUtil.Loc("PSModulePathLocations"));
                        }
                    }

                    Trace.Info("Initializing message listener - establishing connection to Azure DevOps");
                    _listener = HostContext.GetService<IMessageListener>();
                    if (!await _listener.CreateSessionAsync(HostContext.AgentShutdownToken))
                    {
                        Trace.Error("Failed to create session with Azure DevOps");
                        return Constants.Agent.ReturnCode.TerminatedError;
                    }

                    HostContext.WritePerfCounter("SessionCreated");
                    Trace.Info("Session created successfully - agent is now listening for jobs");

                    // Check feature flags for enhanced logging and other runtime features
                    await InitializeRuntimeFeatures();

                    _term.WriteLine(StringUtil.Loc("ListenForJobs", DateTime.UtcNow));

                    IJobDispatcher jobDispatcher = null;
                    CancellationTokenSource messageQueueLoopTokenSource = CancellationTokenSource.CreateLinkedTokenSource(HostContext.AgentShutdownToken);
                    CancellationTokenSource keepAliveToken = CancellationTokenSource.CreateLinkedTokenSource(HostContext.AgentShutdownToken);
                    try
                    {
                        Trace.Info("Initializing job notification service for real-time updates");
                        var notification = HostContext.GetService<IJobNotification>();
                        if (!String.IsNullOrEmpty(settings.NotificationSocketAddress))
                        {
                            notification.StartClient(settings.NotificationSocketAddress, settings.MonitorSocketAddress);
                        }
                        else
                        {
                            notification.StartClient(settings.NotificationPipeName, settings.MonitorSocketAddress, HostContext.AgentShutdownToken);
                        }
                        // this is not a reliable way to disable auto update.
                        // we need server side work to really enable the feature
                        // https://github.com/Microsoft/vsts-agent/issues/446 (Feature: Allow agent / pool to opt out of automatic updates)
                        bool disableAutoUpdate = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("agent.disableupdate"));
                        bool autoUpdateInProgress = false;
                        Task<bool> selfUpdateTask = null;
                        bool runOnceJobReceived = false;
                        jobDispatcher = HostContext.CreateService<IJobDispatcher>();
                        TaskAgentMessage previuosMessage = null;

                        _ = _listener.KeepAlive(keepAliveToken.Token);

                        Trace.Info("Starting message processing loop - agent ready to receive jobs");
                        while (!HostContext.AgentShutdownToken.IsCancellationRequested)
                        {
                            TaskAgentMessage message = null;
                            bool skipMessageDeletion = false;
                            try
                            {
                                Trace.Info("Next message wait initiated - Agent ready to receive next message from server");
                                Task<TaskAgentMessage> getNextMessage = _listener.GetNextMessageAsync(messageQueueLoopTokenSource.Token);
                                if (autoUpdateInProgress)
                                {
                                    Trace.Verbose("Auto update task running at backend, waiting for getNextMessage or selfUpdateTask to finish.");
                                    Task completeTask = await Task.WhenAny(getNextMessage, selfUpdateTask);
                                    if (completeTask == selfUpdateTask)
                                    {
                                        autoUpdateInProgress = false;

                                        bool agentUpdated = false;
                                        try
                                        {
                                            agentUpdated = await selfUpdateTask;
                                        }
                                        catch (Exception ex)
                                        {
                                            Trace.Info($"Ignore agent update exception. {ex}");
                                        }

                                        if (agentUpdated)
                                        {
                                            Trace.Info("Auto update task finished at backend, an agent update is ready to apply exit the current agent instance.");
                                            Trace.Info("Stop message queue looping.");
                                            messageQueueLoopTokenSource.Cancel();
                                            try
                                            {
                                                await getNextMessage;
                                            }
                                            catch (Exception ex)
                                            {
                                                Trace.Info($"Ignore any exception after cancel message loop. {ex}");
                                            }

                                            if (runOnce)
                                            {
                                                return Constants.Agent.ReturnCode.RunOnceAgentUpdating;
                                            }
                                            else
                                            {
                                                return Constants.Agent.ReturnCode.AgentUpdating;
                                            }
                                        }
                                        else
                                        {
                                            Trace.Info("Auto update task finished at backend, there is no available agent update needs to apply, continue message queue looping.");
                                        }

                                        message = previuosMessage;// if agent wasn't updated it's needed to process the previous message
                                        previuosMessage = null;
                                    }
                                }

                                if (runOnceJobReceived)
                                {
                                    Trace.Verbose("One time used agent has start running its job, waiting for getNextMessage or the job to finish.");
                                    Task completeTask = await Task.WhenAny(getNextMessage, jobDispatcher.RunOnceJobCompleted.Task);
                                    if (completeTask == jobDispatcher.RunOnceJobCompleted.Task)
                                    {
                                        Trace.Info("Job has finished at backend, the agent will exit since it is running under onetime use mode.");
                                        Trace.Info("Stop message queue looping.");
                                        messageQueueLoopTokenSource.Cancel();
                                        try
                                        {
                                            await getNextMessage;
                                        }
                                        catch (Exception ex)
                                        {
                                            Trace.Info($"Ignore any exception after cancel message loop. {ex}");
                                        }

                                        return Constants.Agent.ReturnCode.Success;
                                    }
                                }

                                message ??= await getNextMessage; //get next message
                                Trace.Info($"Next message wait completed - Received message from server: {message.MessageType}");
                                HostContext.WritePerfCounter($"MessageReceived_{message.MessageType}");
                                if (string.Equals(message.MessageType, AgentRefreshMessage.MessageType, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (disableAutoUpdate)
                                    {
                                        Trace.Info("Auto-update handling - Refresh message received but skipping autoupdate since agent.disableupdate is set");
                                    }
                                    else
                                    {
                                        if (autoUpdateInProgress == false)
                                        {
                                            autoUpdateInProgress = true;
                                            var agentUpdateMessage = JsonUtility.FromString<AgentRefreshMessage>(message.Body);
                                            var selfUpdater = HostContext.GetService<ISelfUpdater>();
                                            selfUpdateTask = selfUpdater.SelfUpdate(agentUpdateMessage, jobDispatcher, !runOnce && HostContext.StartupType != StartupType.Service, HostContext.AgentShutdownToken);
                                            Trace.Info(StringUtil.Format("Agent update handling - Self-update task initiated, target version: {0}", agentUpdateMessage.TargetVersion));
                                        }
                                        else
                                        {
                                            Trace.Info("Agent update message received, skip autoupdate since a previous autoupdate is already running.");
                                        }
                                    }
                                }
                                else if (string.Equals(message.MessageType, JobRequestMessageTypes.AgentJobRequest, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(message.MessageType, JobRequestMessageTypes.PipelineAgentJobRequest, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (autoUpdateInProgress)
                                    {
                                        previuosMessage = message;
                                    }

                                    if (autoUpdateInProgress || runOnceJobReceived)
                                    {
                                        skipMessageDeletion = true;
                                        Trace.Info($"Skip message deletion for job request message '{message.MessageId}'.");
                                    }
                                    else
                                    {
                                        Pipelines.AgentJobRequestMessage pipelineJobMessage = null;
                                        switch (message.MessageType)
                                        {
                                            case JobRequestMessageTypes.AgentJobRequest:
                                                Trace.Verbose("Converting legacy job message format to pipeline format");
                                                var legacyJobMessage = JsonUtility.FromString<AgentJobRequestMessage>(message.Body);
                                                pipelineJobMessage = Pipelines.AgentJobRequestMessageUtil.Convert(legacyJobMessage);
                                                break;
                                            case JobRequestMessageTypes.PipelineAgentJobRequest:
                                                Trace.Verbose("Processing pipeline job message for execution");
                                                pipelineJobMessage = JsonUtility.FromString<Pipelines.AgentJobRequestMessage>(message.Body);
                                                break;
                                        }

                                        Trace.Info("Dispatching job to worker process for execution");
                                        jobDispatcher.Run(pipelineJobMessage, runOnce);
                                        if (runOnce)
                                        {
                                            Trace.Info("One time used agent received job message.");
                                            runOnceJobReceived = true;
                                        }
                                    }
                                }
                                else if (string.Equals(message.MessageType, JobCancelMessage.MessageType, StringComparison.OrdinalIgnoreCase))
                                {
                                    Trace.Verbose("Processing job cancellation request from Azure DevOps");
                                    var cancelJobMessage = JsonUtility.FromString<JobCancelMessage>(message.Body);
                                    bool jobCancelled = jobDispatcher.Cancel(cancelJobMessage);
                                    skipMessageDeletion = (autoUpdateInProgress || runOnceJobReceived) && !jobCancelled;

                                    if (skipMessageDeletion)
                                    {
                                        Trace.Info($"Skip message deletion for cancellation message '{message.MessageId}'.");
                                    }
                                }
                                else if (string.Equals(message.MessageType, JobMetadataMessage.MessageType, StringComparison.OrdinalIgnoreCase))
                                {
                                    Trace.Info("Processing job metadata update from Azure DevOps");
                                    var metadataMessage = JsonUtility.FromString<JobMetadataMessage>(message.Body);
                                    jobDispatcher.MetadataUpdate(metadataMessage);
                                }
                                else
                                {
                                    Trace.Error($"Received message {message.MessageId} with unsupported message type {message.MessageType}.");
                                }
                            }
                            catch (AggregateException e)
                            {
                                Trace.Error($"Exception occurred while processing message from queue: {e.Message}");
                                ExceptionsUtil.HandleAggregateException((AggregateException)e, (message) => Trace.Error(message));
                            }
                            finally
                            {
                                if (!skipMessageDeletion && message != null)
                                {
                                    Trace.Info($"Message deletion from queue initiated - Deleting processed message: {message.MessageId}");
                                    try
                                    {
                                        await _listener.DeleteMessageAsync(message);
                                        Trace.Info($"Message deletion completed - Message {message.MessageId} successfully removed from queue");
                                    }
                                    catch (Exception ex)
                                    {
                                        Trace.Error($"Catch exception during delete message from message queue. message id: {message.MessageId}");
                                        Trace.Error(ex);
                                    }
                                    finally
                                    {
                                        Trace.Info("Message cleanup completed - Message reference cleared, ready for next message");
                                        message = null;
                                    }
                                }
                                else
                                {
                                    Trace.Info("Message deletion skipped - Either skip flag set or no message to delete");
                                }
                            }
                        }
                    }
                    finally
                    {
                        Trace.Info("Beginning agent shutdown sequence - cleaning up resources");
                        keepAliveToken.Dispose();

                        if (jobDispatcher != null)
                        {
                            Trace.Info("Shutting down job dispatcher - terminating active jobs");
                            await jobDispatcher.ShutdownAsync();
                        }

                        Trace.Info("Cleaning up agent listener session - disconnecting from Azure DevOps");
                        //TODO: make sure we don't mask more important exception
                        await _listener.DeleteSessionAsync();

                        messageQueueLoopTokenSource.Dispose();
                    }
                }
                catch (TaskAgentAccessTokenExpiredException)
                {
                    Trace.Info("Agent OAuth token has been revoked - shutting down gracefully");
                }

                Trace.Info("Agent run completed successfully - exiting with success code");
                return Constants.Agent.ReturnCode.Success;
            }
        }

        private void PrintUsage(CommandSettings command)
        {
            string ext = "sh";
            if (PlatformUtil.RunningOnWindows)
            {
                ext = "cmd";
            }
            string commonHelp = StringUtil.Loc("CommandLineHelp_Common");
            string envHelp = StringUtil.Loc("CommandLineHelp_Env");
            if (command.IsConfigureCommand())
            {
                _term.WriteLine(StringUtil.Loc("CommandLineHelp_Configure", Path.DirectorySeparatorChar, ext, commonHelp, envHelp));
            }
            else if (command.IsRemoveCommand())
            {
                _term.WriteLine(StringUtil.Loc("CommandLineHelp_Remove", Path.DirectorySeparatorChar, ext, commonHelp, envHelp));
            }
            else
            {
                _term.WriteLine(StringUtil.Loc("CommandLineHelp", Path.DirectorySeparatorChar, ext));
            }
        }

        private void PrintBanner()
        {
            _term.WriteLine(_banner);
        }

        private static string _banner = string.Format(@"
  ___                      ______ _            _ _
 / _ \                     | ___ (_)          | (_)
/ /_\ \_____   _ _ __ ___  | |_/ /_ _ __   ___| |_ _ __   ___  ___
|  _  |_  / | | | '__/ _ \ |  __/| | '_ \ / _ \ | | '_ \ / _ \/ __|
| | | |/ /| |_| | | |  __/ | |   | | |_) |  __/ | | | | |  __/\__ \
\_| |_/___|\__,_|_|  \___| \_|   |_| .__/ \___|_|_|_| |_|\___||___/
                                   | |
        agent v{0,-10}          |_|          (commit {1})
", BuildConstants.AgentPackage.Version, BuildConstants.Source.CommitHash.Substring(0, 7));
    }
}