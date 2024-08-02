// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.Expressions;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.VisualStudio.Services.Agent.Util;
using System.Linq;
using System.Diagnostics;
using Agent.Sdk;
using Agent.Sdk.Knob;
using Newtonsoft.Json;
using Microsoft.VisualStudio.Services.Agent.Worker.Telemetry;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    public interface IJobExtension : IExtension
    {
        HostTypes HostType { get; }
        Task<List<IStep>> InitializeJob(IExecutionContext jobContext, Pipelines.AgentJobRequestMessage message);
        Task FinalizeJob(IExecutionContext jobContext);
        string GetRootedPath(IExecutionContext context, string path);
        void ConvertLocalPath(IExecutionContext context, string localPath, out string repoName, out string sourcePath);
    }

    public abstract class JobExtension : AgentService, IJobExtension
    {
        private readonly HashSet<string> _existingProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _processCleanup;
        private string _processLookupId = $"vsts_{Guid.NewGuid()}";

        private bool _taskKeyCleanup;

        public abstract HostTypes HostType { get; }

        public abstract Type ExtensionType { get; }

        // Anything job extension want to do before building the steps list.
        public abstract void InitializeJobExtension(IExecutionContext context, IList<Pipelines.JobStep> steps, Pipelines.WorkspaceOptions workspace);

        // Anything job extension want to add to pre-job steps list. This will be deprecated when GetSource move to a task.
        public abstract IStep GetExtensionPreJobStep(IExecutionContext jobContext);

        // Anything job extension want to add to post-job steps list. This will be deprecated when GetSource move to a task.
        public abstract IStep GetExtensionPostJobStep(IExecutionContext jobContext);

        public abstract string GetRootedPath(IExecutionContext context, string path);

        public abstract void ConvertLocalPath(IExecutionContext context, string localPath, out string repoName, out string sourcePath);

        // download all required tasks.
        // make sure all task's condition inputs are valid.
        // build up three list of steps for jobrunner. (pre-job, job, post-job)
        public async Task<List<IStep>> InitializeJob(IExecutionContext jobContext, Pipelines.AgentJobRequestMessage message)
        {
            Trace.Entering();
            ArgUtil.NotNull(jobContext, nameof(jobContext));
            ArgUtil.NotNull(message, nameof(message));

            // create a new timeline record node for 'Initialize job'
            IExecutionContext context = jobContext.CreateChild(Guid.NewGuid(), StringUtil.Loc("InitializeJob"), $"{nameof(JobExtension)}_Init");

            List<IStep> preJobSteps = new List<IStep>();
            List<IStep> jobSteps = new List<IStep>();
            List<IStep> postJobSteps = new List<IStep>();
            using (var register = jobContext.CancellationToken.Register(() => { context.CancelToken(); }))
            {
                try
                {
                    context.Start();
                    context.Section(StringUtil.Loc("StepStarting", StringUtil.Loc("InitializeJob")));

                    PackageVersion agentVersion = new PackageVersion(BuildConstants.AgentPackage.Version);

                    // Check if a system supports .NET 8
                    try
                    {
                        Trace.Verbose("Checking if your system supports .NET 8");

                        // Check version of the system
                        if (!await PlatformUtil.IsNetVersionSupported("net8"))
                        {
                            string systemId = PlatformUtil.GetSystemId();
                            SystemVersion systemVersion = PlatformUtil.GetSystemVersion();
                            context.Warning(StringUtil.Loc("UnsupportedOsVersionByNet8", $"{systemId} {systemVersion}"));
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.Error($"Error has occurred while checking if system supports .NET 8: {ex}");
                        context.Warning(ex.Message);
                    }

                    // Set agent version variable.
                    context.SetVariable(Constants.Variables.Agent.Version, BuildConstants.AgentPackage.Version);
                    context.Output(StringUtil.Loc("AgentNameLog", context.Variables.Get(Constants.Variables.Agent.Name)));
                    context.Output(StringUtil.Loc("AgentMachineNameLog", context.Variables.Get(Constants.Variables.Agent.MachineName)));
                    context.Output(StringUtil.Loc("AgentVersion", BuildConstants.AgentPackage.Version));

                    // Machine specific setup info
                    OutputSetupInfo(context);
                    OutputImageVersion(context);
                    context.Output(StringUtil.Loc("UserNameLog", System.Environment.UserName));

                    // Print proxy setting information for better diagnostic experience
                    var agentWebProxy = HostContext.GetService<IVstsAgentWebProxy>();
                    if (!string.IsNullOrEmpty(agentWebProxy.ProxyAddress))
                    {
                        context.Output(StringUtil.Loc("AgentRunningBehindProxy", agentWebProxy.ProxyAddress));
                    }

                    // Give job extension a chance to initialize
                    Trace.Info($"Run initial step from extension {this.GetType().Name}.");
                    InitializeJobExtension(context, message?.Steps, message?.Workspace);

                    if (AgentKnobs.ForceCreateTasksDirectory.GetValue(context).AsBoolean())
                    {
                        var tasksDir = HostContext.GetDirectory(WellKnownDirectory.Tasks);
                        try
                        {
                            Trace.Info($"Pre-creating {tasksDir} directory");
                            Directory.CreateDirectory(tasksDir);
                            IOUtil.ValidateExecutePermission(tasksDir);
                        }
                        catch (Exception ex)
                        {
                            Trace.Error(ex);
                            context.Error(ex);
                        }
                    }

                    // Download tasks if not already in the cache
                    Trace.Info("Downloading task definitions.");
                    var taskManager = HostContext.GetService<ITaskManager>();
                    await taskManager.DownloadAsync(context, message.Steps);

                    if (!AgentKnobs.DisableNode6Tasks.GetValue(context).AsBoolean() && !PlatformUtil.RunningOnAlpine)
                    {
                        Trace.Info("Downloading Node 6 runner.");
                        var nodeUtil = new NodeJsUtil(HostContext);
                        await nodeUtil.DownloadNodeRunnerAsync(context, register.Token);
                    }

                    // Parse all Task conditions.
                    Trace.Info("Parsing all task's condition inputs.");
                    var expression = HostContext.GetService<IExpressionManager>();
                    Dictionary<Guid, IExpressionNode> taskConditionMap = new Dictionary<Guid, IExpressionNode>();
                    foreach (var task in message.Steps.OfType<Pipelines.TaskStep>())
                    {
                        IExpressionNode condition;
                        if (!string.IsNullOrEmpty(task.Condition))
                        {
                            context.Debug($"Task '{task.DisplayName}' has following condition: '{task.Condition}'.");
                            condition = expression.Parse(context, task.Condition);
                        }
                        else
                        {
                            condition = ExpressionManager.Succeeded;
                        }

                        task.DisplayName = context.Variables.ExpandValue(nameof(task.DisplayName), task.DisplayName);

                        taskConditionMap[task.Id] = condition;
                    }
                    context.Output("Checking job knob settings.");
                    foreach (var knob in Knob.GetAllKnobsFor<AgentKnobs>())
                    {
                        var value = knob.GetValue(jobContext);
                        if (value.Source.GetType() != typeof(BuiltInDefaultKnobSource))
                        {
                            var tag = "";
                            if (knob.IsDeprecated)
                            {
                                tag = "(DEPRECATED)";
                            }
                            else if (knob.IsExperimental)
                            {
                                tag = "(EXPERIMENTAL)";
                            }
                            var stringValue = value.AsString();
                            if (knob is SecretKnob)
                            {
                                HostContext.SecretMasker.AddValue(stringValue, $"JobExtension_InitializeJob_{knob.Name}");
                            }
                            var outputLine = $"   Knob: {knob.Name} = {stringValue} Source: {value.Source.GetDisplayString()} {tag}";

                            if (knob.IsDeprecated)
                            {
                                context.Warning(outputLine);

                                string deprecationInfo = (knob as DeprecatedKnob).DeprecationInfo;
                                if (!string.IsNullOrEmpty(deprecationInfo))
                                {
                                    context.Warning(deprecationInfo);
                                }
                            }
                            else
                            {
                                context.Output(outputLine);
                            }
                        }
                    }
                    context.Output("Finished checking job knob settings.");

                    // Ensure that we send git telemetry before potential path env changes during the pipeline execution
                    var isSelfHosted = StringUtil.ConvertToBoolean(jobContext.Variables.Get(Constants.Variables.Agent.IsSelfHosted));
                    if (PlatformUtil.RunningOnWindows && isSelfHosted)
                    {
                        try
                        {
                            var windowsPreinstalledGitCommand = jobContext.AsyncCommands.Find(c => c != null && c.Name == Constants.AsyncExecution.Commands.Names.WindowsPreinstalledGitTelemetry);
                            if (windowsPreinstalledGitCommand != null)
                            {
                                await windowsPreinstalledGitCommand.WaitAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log the error
                            Trace.Info($"Caught exception from async command WindowsPreinstalledGitTelemetry: {ex}");
                        }
                    }

                    if (PlatformUtil.RunningOnWindows)
                    {
                        // This is for internal testing and is not publicly supported. This will be removed from the agent at a later time.
                        var prepareScript = Environment.GetEnvironmentVariable("VSTS_AGENT_INIT_INTERNAL_TEMP_HACK");
                        ServiceEndpoint systemConnection = context.Endpoints.Single(x => string.Equals(x.Name, WellKnownServiceEndpointNames.SystemVssConnection, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(prepareScript) && context.StepTarget() is HostInfo)
                        {
                            var prepareStep = new ManagementScriptStep(
                                scriptPath: prepareScript,
                                condition: ExpressionManager.Succeeded,
                                displayName: "Agent Initialization");

                            Trace.Verbose($"Adding agent init script step.");
                            prepareStep.Initialize(HostContext);
                            prepareStep.ExecutionContext = jobContext?.CreateChild(Guid.NewGuid(), prepareStep.DisplayName, nameof(ManagementScriptStep));
                            prepareStep.AccessToken = systemConnection.Authorization.Parameters["AccessToken"];
                            prepareStep.Condition = ExpressionManager.Succeeded;
                            preJobSteps.Add(prepareStep);
                        }

                        string gitVersion = null;

                        if (AgentKnobs.UseGit2_39_4.GetValue(jobContext).AsBoolean())
                        {
                            gitVersion = "2.39.4";
                        }
                        else if (AgentKnobs.UseGit2_42_0_2.GetValue(jobContext).AsBoolean())
                        {
                            gitVersion = "2.42.0.2";
                        }

                        if (gitVersion is not null)
                        {
                            context.Debug($"Downloading Git v{gitVersion}");
                            var gitManager = HostContext.GetService<IGitManager>();
                            await gitManager.DownloadAsync(context, gitVersion);
                        }
                    }

                    // build up 3 lists of steps, pre-job, job, post-job
                    Stack<IStep> postJobStepsBuilder = new Stack<IStep>();
                    Dictionary<Guid, Variables> taskVariablesMapping = new Dictionary<Guid, Variables>();

                    if (context.Containers.Count > 0 || context.SidecarContainers.Count > 0)
                    {
                        var containerProvider = HostContext.GetService<IContainerOperationProvider>();
                        var containers = new List<ContainerInfo>();
                        containers.AddRange(context.Containers);
                        containers.AddRange(context.SidecarContainers);

                        preJobSteps.Add(new JobExtensionRunner(runAsync: containerProvider.StartContainersAsync,
                                                                          condition: ExpressionManager.Succeeded,
                                                                          displayName: StringUtil.Loc("InitializeContainer"),
                                                                          data: containers));
                        postJobStepsBuilder.Push(new JobExtensionRunner(runAsync: containerProvider.StopContainersAsync,
                                                                        condition: ExpressionManager.Always,
                                                                        displayName: StringUtil.Loc("StopContainer"),
                                                                        data: containers));
                    }

                    Dictionary<Guid, List<TaskRestrictions>> taskRestrictionsMap = new Dictionary<Guid, List<TaskRestrictions>>();
                    foreach (var task in message?.Steps.OfType<Pipelines.TaskStep>())
                    {
                        var taskDefinition = taskManager.Load(task);

                        // Collect any task restrictions from the definition or step
                        var restrictions = new List<TaskRestrictions>();
                        if (taskDefinition.Data.Restrictions != null)
                        {
                            restrictions.Add(new TaskDefinitionRestrictions(taskDefinition.Data));
                        }
                        if (string.Equals(WellKnownStepTargetStrings.Restricted, task.Target?.Commands, StringComparison.OrdinalIgnoreCase))
                        {
                            restrictions.Add(new TaskRestrictions() { Commands = new TaskCommandRestrictions() { Mode = TaskCommandMode.Restricted } });
                        }
                        if (task.Target?.SettableVariables != null)
                        {
                            restrictions.Add(new TaskRestrictions() { SettableVariables = task.Target.SettableVariables });
                        }
                        taskRestrictionsMap[task.Id] = restrictions;

                        List<string> warnings;
                        taskVariablesMapping[task.Id] = new Variables(HostContext, new Dictionary<string, VariableValue>(), out warnings);

                        // Add pre-job steps from Tasks
                        if (taskDefinition.Data?.PreJobExecution != null)
                        {
                            Trace.Info($"Adding Pre-Job {task.DisplayName}.");
                            var taskRunner = HostContext.CreateService<ITaskRunner>();
                            taskRunner.Task = task;
                            taskRunner.Stage = JobRunStage.PreJob;
                            taskRunner.Condition = taskConditionMap[task.Id];
                            preJobSteps.Add(taskRunner);
                        }

                        // Add execution steps from Tasks
                        if (taskDefinition.Data?.Execution != null)
                        {
                            Trace.Verbose($"Adding {task.DisplayName}.");
                            var taskRunner = HostContext.CreateService<ITaskRunner>();
                            taskRunner.Task = task;
                            taskRunner.Stage = JobRunStage.Main;
                            taskRunner.Condition = taskConditionMap[task.Id];
                            jobSteps.Add(taskRunner);
                        }

                        // Add post-job steps from Tasks
                        if (taskDefinition.Data?.PostJobExecution != null)
                        {
                            Trace.Verbose($"Adding Post-Job {task.DisplayName}.");
                            var taskRunner = HostContext.CreateService<ITaskRunner>();
                            taskRunner.Task = task;
                            taskRunner.Stage = JobRunStage.PostJob;
                            taskRunner.Condition = ExpressionManager.Always;
                            postJobStepsBuilder.Push(taskRunner);
                        }
                    }

                    // Add pre-job step from Extension
                    Trace.Info("Adding pre-job step from extension.");
                    var extensionPreJobStep = GetExtensionPreJobStep(jobContext);
                    if (extensionPreJobStep != null)
                    {
                        preJobSteps.Add(extensionPreJobStep);
                    }

                    // Add post-job step from Extension
                    Trace.Info("Adding post-job step from extension.");
                    var extensionPostJobStep = GetExtensionPostJobStep(jobContext);
                    if (extensionPostJobStep != null)
                    {
                        postJobStepsBuilder.Push(extensionPostJobStep);
                    }

                    ArgUtil.NotNull(jobContext, nameof(jobContext)); // I am not sure why this is needed, but static analysis flagged all uses of jobContext below this point
                                                                     // create execution context for all pre-job steps
                    foreach (var step in preJobSteps)
                    {
                        if (PlatformUtil.RunningOnWindows && step is ManagementScriptStep)
                        {
                            continue;
                        }

                        if (step is JobExtensionRunner)
                        {
                            JobExtensionRunner extensionStep = step as JobExtensionRunner;
                            ArgUtil.NotNull(extensionStep, extensionStep.DisplayName);
                            Guid stepId = Guid.NewGuid();
                            extensionStep.ExecutionContext = jobContext.CreateChild(stepId, extensionStep.DisplayName, stepId.ToString("N"));
                        }
                        else if (step is ITaskRunner)
                        {
                            ITaskRunner taskStep = step as ITaskRunner;
                            ArgUtil.NotNull(taskStep, step.DisplayName);
                            taskStep.ExecutionContext = jobContext.CreateChild(
                                Guid.NewGuid(),
                                StringUtil.Loc("PreJob", taskStep.DisplayName),
                                taskStep.Task.Name,
                                taskVariablesMapping[taskStep.Task.Id],
                                outputForward: true,
                                taskRestrictions: taskRestrictionsMap[taskStep.Task.Id]);
                        }
                    }

                    // create task execution context for all job steps from task
                    foreach (var step in jobSteps)
                    {
                        ITaskRunner taskStep = step as ITaskRunner;
                        ArgUtil.NotNull(taskStep, step.DisplayName);
                        taskStep.ExecutionContext = jobContext.CreateChild(
                            taskStep.Task.Id,
                            taskStep.DisplayName,
                            taskStep.Task.Name,
                            taskVariablesMapping[taskStep.Task.Id],
                            outputForward: true,
                            taskRestrictions: taskRestrictionsMap[taskStep.Task.Id]);
                    }

                    // Add post-job steps from Tasks
                    Trace.Info("Adding post-job steps from tasks.");
                    while (postJobStepsBuilder.Count > 0)
                    {
                        postJobSteps.Add(postJobStepsBuilder.Pop());
                    }

                    // create task execution context for all post-job steps from task
                    foreach (var step in postJobSteps)
                    {
                        if (step is JobExtensionRunner)
                        {
                            JobExtensionRunner extensionStep = step as JobExtensionRunner;
                            ArgUtil.NotNull(extensionStep, extensionStep.DisplayName);
                            Guid stepId = Guid.NewGuid();
                            extensionStep.ExecutionContext = jobContext.CreateChild(stepId, extensionStep.DisplayName, stepId.ToString("N"));
                        }
                        else if (step is ITaskRunner)
                        {
                            ITaskRunner taskStep = step as ITaskRunner;
                            ArgUtil.NotNull(taskStep, step.DisplayName);
                            taskStep.ExecutionContext = jobContext.CreateChild(
                                Guid.NewGuid(),
                                StringUtil.Loc("PostJob", taskStep.DisplayName),
                                taskStep.Task.Name,
                                taskVariablesMapping[taskStep.Task.Id],
                                outputForward: true,
                                taskRestrictions: taskRestrictionsMap[taskStep.Task.Id]);
                        }
                    }

                    if (PlatformUtil.RunningOnWindows)
                    {
                        // Add script post steps.
                        // This is for internal testing and is not publicly supported. This will be removed from the agent at a later time.
                        var finallyScript = Environment.GetEnvironmentVariable("VSTS_AGENT_CLEANUP_INTERNAL_TEMP_HACK");
                        if (!string.IsNullOrEmpty(finallyScript) && context.StepTarget() is HostInfo)
                        {
                            var finallyStep = new ManagementScriptStep(
                                scriptPath: finallyScript,
                                condition: ExpressionManager.Always,
                                displayName: "Agent Cleanup");

                            Trace.Verbose($"Adding agent cleanup script step.");
                            finallyStep.Initialize(HostContext);
                            finallyStep.ExecutionContext = jobContext.CreateChild(Guid.NewGuid(), finallyStep.DisplayName, nameof(ManagementScriptStep));
                            finallyStep.Condition = ExpressionManager.Always;
                            ServiceEndpoint systemConnection = context.Endpoints.Single(x => string.Equals(x.Name, WellKnownServiceEndpointNames.SystemVssConnection, StringComparison.OrdinalIgnoreCase));
                            finallyStep.AccessToken = systemConnection.Authorization.Parameters["AccessToken"];
                            postJobSteps.Add(finallyStep);
                        }
                    }

                    if (AgentKnobs.Rosetta2Warning.GetValue(jobContext).AsBoolean())
                    {
                        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                        {
                            if (await PlatformUtil.IsRunningOnAppleSiliconAsX64Async(timeout.Token))
                            {
                                jobContext.Warning(StringUtil.Loc("Rosetta2Warning"));
                            }
                        }
                    }
                    List<IStep> steps = new List<IStep>();
                    steps.AddRange(preJobSteps);
                    steps.AddRange(jobSteps);
                    steps.AddRange(postJobSteps);

                    // Start agent log plugin host process
                    var logPlugin = HostContext.GetService<IAgentLogPlugin>();
                    await logPlugin.StartAsync(context, steps, jobContext.CancellationToken);

                    // Prepare for orphan process cleanup
                    _processCleanup = jobContext.Variables.GetBoolean("process.clean") ?? true;
                    if (_processCleanup)
                    {
                        // Set the VSTS_PROCESS_LOOKUP_ID env variable.
                        context.SetVariable(Constants.ProcessLookupId, _processLookupId, false, false);
                        context.Output("Start tracking orphan processes.");
                        // Take a snapshot of current running processes
                        Dictionary<int, Process> processes = SnapshotProcesses();
                        foreach (var proc in processes)
                        {
                            // Pid_ProcessName
                            _existingProcesses.Add($"{proc.Key}_{proc.Value.ProcessName}");
                        }
                    }
                    _taskKeyCleanup = jobContext.Variables.GetBoolean("process.cleanTaskKey") ?? true;

                    return steps;
                }
                catch (OperationCanceledException ex) when (jobContext.CancellationToken.IsCancellationRequested)
                {
                    // Log the exception and cancel the JobExtension Initialization.
                    if (AgentKnobs.FailJobWhenAgentDies.GetValue(jobContext).AsBoolean() &&
                        HostContext.AgentShutdownToken.IsCancellationRequested)
                    {
                        PublishAgentShutdownTelemetry(jobContext, context);
                        Trace.Error($"Caught Agent Shutdown exception from JobExtension Initialization: {ex.Message}");
                        context.Error(ex);
                        context.Result = TaskResult.Failed;
                        throw;
                    }
                    else
                    {
                        Trace.Error($"Caught cancellation exception from JobExtension Initialization: {ex}");
                        context.Error(ex);
                        context.Result = TaskResult.Canceled;
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    // Log the error and fail the JobExtension Initialization.
                    Trace.Error($"Caught exception from JobExtension Initialization: {ex}");
                    context.Error(ex);
                    context.Result = TaskResult.Failed;
                    throw;
                }
                finally
                {
                    context.Section(StringUtil.Loc("StepFinishing", StringUtil.Loc("InitializeJob")));
                    context.Complete();
                }
            }
        }

        private void PublishAgentShutdownTelemetry(IExecutionContext jobContext, IExecutionContext childContext)
        {
            var telemetryData = new Dictionary<string, string>
            {
                { "JobId", childContext?.Variables?.System_JobId?.ToString() ?? string.Empty },
                { "JobResult", TaskResult.Failed.ToString() },
                { "TracePoint", "110" },
            };

            PublishTelemetry(jobContext, telemetryData, "AgentShutdown");
        }

        public async Task FinalizeJob(IExecutionContext jobContext)
        {
            Trace.Entering();
            ArgUtil.NotNull(jobContext, nameof(jobContext));

            // create a new timeline record node for 'Finalize job'
            IExecutionContext context = jobContext.CreateChild(Guid.NewGuid(), StringUtil.Loc("FinalizeJob"), $"{nameof(JobExtension)}_Final");
            using (var register = jobContext.CancellationToken.Register(() => { context.CancelToken(); }))
            {
                try
                {
                    context.Start();
                    context.Section(StringUtil.Loc("StepStarting", StringUtil.Loc("FinalizeJob")));

                    // Wait for agent log plugin process exits
                    var logPlugin = HostContext.GetService<IAgentLogPlugin>();
                    try
                    {
                        await logPlugin.WaitAsync(context);
                    }
                    catch (Exception ex)
                    {
                        // Log and ignore the error from log plugin finalization.
                        Trace.Error($"Caught exception from log plugin finalization: {ex}");
                        context.Output(ex.Message);
                    }

                    if (_taskKeyCleanup)
                    {
                        context.Output("Cleaning up task key");
                        string taskKeyFile = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Work), ".taskkey");
                        if (File.Exists(taskKeyFile))
                        {
                            try
                            {
                                File.Delete(taskKeyFile);
                            }
                            catch (Exception ex)
                            {
                                Trace.Error($"Caught exception while attempting to delete taskKey file {taskKeyFile}: {ex}");
                                context.Output(ex.Message);
                            }
                        }
                    }

                    if (_processCleanup)
                    {
                        context.Output("Start cleaning up orphan processes.");

                        // Only check environment variable for any process that doesn't run before we invoke our process.
                        Dictionary<int, Process> currentProcesses = SnapshotProcesses();
                        foreach (var proc in currentProcesses)
                        {
                            if (_existingProcesses.Contains($"{proc.Key}_{proc.Value.ProcessName}"))
                            {
                                Trace.Verbose($"Skip existing process. PID: {proc.Key} ({proc.Value.ProcessName})");
                            }
                            else
                            {
                                Trace.Info($"Inspecting process environment variables. PID: {proc.Key} ({proc.Value.ProcessName})");

                                string lookupId = null;
                                try
                                {
                                    lookupId = proc.Value.GetEnvironmentVariable(HostContext, Constants.ProcessLookupId);
                                }
                                catch (Exception ex)
                                {
                                    Trace.Warning($"Ignore exception during read process environment variables: {ex.Message}");
                                    Trace.Verbose(ex.ToString());
                                }

                                if (string.Equals(lookupId, _processLookupId, StringComparison.OrdinalIgnoreCase))
                                {
                                    context.Output($"Terminate orphan process: pid ({proc.Key}) ({proc.Value.ProcessName})");
                                    try
                                    {
                                        proc.Value.Kill();
                                    }
                                    catch (Exception ex)
                                    {
                                        Trace.Error("Catch exception during orphan process cleanup.");
                                        Trace.Error(ex);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log and ignore the error from JobExtension finalization.
                    Trace.Error($"Caught exception from JobExtension finalization: {ex}");
                    context.Output(ex.Message);
                }
                finally
                {
                    context.Section(StringUtil.Loc("StepFinishing", StringUtil.Loc("FinalizeJob")));
                    context.Complete();
                }
            }
        }

        private Dictionary<int, Process> SnapshotProcesses()
        {
            Dictionary<int, Process> snapshot = new Dictionary<int, Process>();
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    // On Windows, this will throw exception on error.
                    // On Linux, this will be NULL on error.
                    if (!string.IsNullOrEmpty(proc.ProcessName))
                    {
                        snapshot[proc.Id] = proc;
                    }
                }
                catch (Exception ex)
                {
                    Trace.Verbose($"Ignore any exception during taking process snapshot of process pid={proc.Id}: '{ex.Message}'.");
                }
            }

            Trace.Info($"Total accessible running process: {snapshot.Count}.");
            return snapshot;
        }

        private void OutputImageVersion(IExecutionContext context)
        {
            string imageVersion = System.Environment.GetEnvironmentVariable(Constants.ImageVersionVariable);
            string jobId = context?.Variables?.System_JobId?.ToString() ?? string.Empty;

            if (imageVersion != null)
            {
                context.Output(StringUtil.Loc("ImageVersionLog", imageVersion));
            }
            else
            {
                Trace.Info($"Image version for job id {jobId} is not set");
            }

            var telemetryData = new Dictionary<string, string>()
            {
                { "JobId", jobId },
                { "ImageVersion", imageVersion },
            };

            PublishTelemetry(context, telemetryData, "ImageVersionTelemetry");
        }

        private void OutputSetupInfo(IExecutionContext context)
        {
            try
            {
                var configurationStore = HostContext.GetService<IConfigurationStore>();

                foreach (var info in configurationStore.GetSetupInfo())
                {
                    if (!string.IsNullOrEmpty(info.Detail))
                    {
                        var groupName = info.Group;
                        if (string.IsNullOrEmpty(groupName))
                        {
                            groupName = "Machine Setup Info";
                        }

                        context.Output($"##[group]{groupName}");
                        var multiLines = info.Detail.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
                        foreach (var line in multiLines)
                        {
                            context.Output(line);
                        }
                        context.Output("##[endgroup]");
                    }
                }
            }
            catch (Exception ex)
            {
                context.Output($"Fail to load and print machine setup info: {ex.Message}");
                Trace.Error(ex);
            }
        }

        private void PublishTelemetry(IExecutionContext context, Dictionary<string, string> telemetryData, string feature)
        {
            try
            {
                var cmd = new Command("telemetry", "publish");
                cmd.Data = JsonConvert.SerializeObject(telemetryData, Formatting.None);
                cmd.Properties.Add("area", "PipelinesTasks");
                cmd.Properties.Add("feature", feature);

                var publishTelemetryCmd = new TelemetryCommandExtension();
                publishTelemetryCmd.Initialize(HostContext);
                publishTelemetryCmd.ProcessCommand(context, cmd);
            }
            catch (Exception ex)
            {
                Trace.Warning($"Unable to publish telemetry data. Exception: {ex}");
            }
        }
    }

    public class UnsupportedOsException : Exception
    {
        public UnsupportedOsException(string message) : base(message) { }
    }
}