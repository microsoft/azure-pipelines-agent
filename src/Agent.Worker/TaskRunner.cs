// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Agent.Sdk.Knob;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.Expressions;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.VisualStudio.Services.Agent.Worker.Telemetry;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Newtonsoft.Json;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    public enum JobRunStage
    {
        PreJob,
        Main,
        PostJob,
    }

    [ServiceLocator(Default = typeof(TaskRunner))]
    public interface ITaskRunner : IStep, IAgentService
    {
        JobRunStage Stage { get; set; }
        Pipelines.TaskStep Task { get; set; }
    }

    public sealed class TaskRunner : AgentService, ITaskRunner
    {
        public JobRunStage Stage { get; set; }

        public IExpressionNode Condition { get; set; }

        public bool ContinueOnError => Task?.ContinueOnError ?? default(bool);

        public string DisplayName => Task?.DisplayName;

        public bool Enabled => Task?.Enabled ?? default(bool);

        public IExecutionContext ExecutionContext { get; set; }

        public Pipelines.TaskStep Task { get; set; }

        public TimeSpan? Timeout => (Task?.TimeoutInMinutes ?? 0) > 0 ? (TimeSpan?)TimeSpan.FromMinutes(Task.TimeoutInMinutes) : null;

        public Pipelines.StepTarget Target => Task?.Target;

        const int RetryCountOnTaskFailureLimit = 10;

        public async Task RunAsync()
        {
            // Validate args.
            Trace.Entering();
            ArgUtil.NotNull(ExecutionContext, nameof(ExecutionContext));
            ArgUtil.NotNull(ExecutionContext.Variables, nameof(ExecutionContext.Variables));
            ArgUtil.NotNull(Task, nameof(Task));

            bool logTaskNameInUserAgent = AgentKnobs.LogTaskNameInUserAgent.GetValue(ExecutionContext).AsBoolean();

            if (logTaskNameInUserAgent)
            {
                VssUtil.PushTaskIntoAgentInfo(Task.Name ?? "", Task.Reference?.Version ?? "");
            }

            try
            {
                await RunAsyncInternal();
            }
            finally
            {
                if (logTaskNameInUserAgent)
                {
                    VssUtil.RemoveTaskFromAgentInfo();
                }
            }
        }

        private async Task RunAsyncInternal()
        {
            var taskManager = HostContext.GetService<ITaskManager>();
            var handlerFactory = HostContext.GetService<IHandlerFactory>();

            Trace.Info($"Allow publishing telemetry for {Task.Reference.Name}@{Task.Reference.Version} task: {IsTelemetryPublishRequired()}");

            // Enable skip for string translator in case of checkout task.
            // It's required for support of multiply checkout tasks with repo alias "self" in container jobs. Reported in issue 3520.
            this.ExecutionContext.Variables.Set(Constants.Variables.Task.SkipTranslatorForCheckout, this.Task.IsCheckoutTask().ToString());

            // Set the task id and display name variable.
            using (var scope = ExecutionContext.Variables.CreateScope())
            {
                scope.Set(Constants.Variables.Task.DisplayName, DisplayName);
                scope.Set(Constants.Variables.Task.PublishTelemetry, IsTelemetryPublishRequired().ToString());
                scope.Set(WellKnownDistributedTaskVariables.TaskInstanceId, Task.Id.ToString("D"));
                scope.Set(WellKnownDistributedTaskVariables.TaskDisplayName, DisplayName);
                scope.Set(WellKnownDistributedTaskVariables.TaskInstanceName, Task.Name);

                // Load the task definition and choose the handler.
                // TODO: Add a try catch here to give a better error message.
                Definition definition = taskManager.Load(Task);
                ArgUtil.NotNull(definition, nameof(definition));

                // Verify Signatures and Re-Extract Tasks if neccessary
                await VerifyTask(taskManager, definition);

                // Print out task metadata
                PrintTaskMetaData(definition);

                ExecutionData currentExecution = null;
                switch (Stage)
                {
                    case JobRunStage.PreJob:
                        currentExecution = definition.Data?.PreJobExecution;
                        break;
                    case JobRunStage.Main:
                        currentExecution = definition.Data?.Execution;
                        break;
                    case JobRunStage.PostJob:
                        currentExecution = definition.Data?.PostJobExecution;
                        break;
                };

                HandlerData handlerData = GetHandlerData(ExecutionContext, currentExecution, PlatformUtil.HostOS);

                if (handlerData == null)
                {
                    if (PlatformUtil.RunningOnWindows)
                    {
                        throw new InvalidOperationException(StringUtil.Loc("SupportedTaskHandlerNotFoundWindows", $"{PlatformUtil.HostOS}({PlatformUtil.HostArchitecture})"));
                    }

                    throw new InvalidOperationException(StringUtil.Loc("SupportedTaskHandlerNotFoundLinux"));
                }
                Trace.Info($"Handler data is of type {handlerData}");

                PublishTelemetry(definition, handlerData);

                Variables runtimeVariables = ExecutionContext.Variables;
                IStepHost stepHost = HostContext.CreateService<IDefaultStepHost>();
                var stepTarget = ExecutionContext.StepTarget();
                // Setup container stephost and the right runtime variables for running job inside container.
                if (stepTarget is ContainerInfo containerTarget)
                {
                    if (Stage == JobRunStage.PostJob
                        && AgentKnobs.SkipPostExeceutionIfTargetContainerStopped.GetValue(ExecutionContext).AsBoolean())
                    {
                        try
                        {
                            // Check that the target container is still running, if not Skip task execution
                            IDockerCommandManager dockerManager = HostContext.GetService<IDockerCommandManager>();
                            bool isContainerRunning = await dockerManager.IsContainerRunning(ExecutionContext, containerTarget.ContainerId);

                            if (!isContainerRunning)
                            {
                                ExecutionContext.Result = TaskResult.Skipped;
                                ExecutionContext.ResultCode = $"Target container - {containerTarget.ContainerName} has been stopped, task post-execution will be skipped";
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            ExecutionContext.Write(WellKnownTags.Warning, $"Failed to check container state for task post-execution. Exception: {ex}");
                        }
                    }

                    if (handlerData is AgentPluginHandlerData)
                    {
                        // plugin handler always runs on the Host, the runtime variables needs to the variable works on the Host, ex: file path variable System.DefaultWorkingDirectory
                        Dictionary<string, VariableValue> variableCopy = new Dictionary<string, VariableValue>(StringComparer.OrdinalIgnoreCase);
                        foreach (var publicVar in ExecutionContext.Variables.Public)
                        {
                            variableCopy[publicVar.Name] = new VariableValue(stepTarget.TranslateToHostPath(publicVar.Value));
                        }
                        foreach (var secretVar in ExecutionContext.Variables.Private)
                        {
                            variableCopy[secretVar.Name] = new VariableValue(stepTarget.TranslateToHostPath(secretVar.Value), true);
                        }

                        List<string> expansionWarnings;
                        runtimeVariables = new Variables(HostContext, variableCopy, out expansionWarnings);
                        expansionWarnings?.ForEach(x => ExecutionContext.Warning(x));
                    }
                    else if (handlerData is BaseNodeHandlerData || handlerData is PowerShell3HandlerData)
                    {
                        // Only the node, node10, and powershell3 handlers support running inside container.
                        // Make sure required container is already created.
                        ArgUtil.NotNullOrEmpty(containerTarget.ContainerId, nameof(containerTarget.ContainerId));
                        var containerStepHost = HostContext.CreateService<IContainerStepHost>();
                        containerStepHost.Container = containerTarget;
                        stepHost = containerStepHost;
                    }
                    else
                    {
                        throw new NotSupportedException(String.Format("Task '{0}' is using legacy execution handler '{1}' which is not supported in container execution flow.", definition.Data.FriendlyName, handlerData.GetType().ToString()));
                    }
                }

                // Load the default input values from the definition.
                Trace.Verbose("Loading default inputs.");
                var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var input in (definition.Data?.Inputs ?? new TaskInputDefinition[0]))
                {
                    string key = input?.Name?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(key))
                    {
                        if (AgentKnobs.DisableInputTrimming.GetValue(ExecutionContext).AsBoolean())
                        {
                            inputs[key] = input.DefaultValue ?? string.Empty;
                        }
                        else
                        {
                            inputs[key] = input.DefaultValue?.Trim() ?? string.Empty;
                        }
                    }
                }

                // Merge the instance inputs.
                Trace.Verbose("Loading instance inputs.");
                foreach (var input in (Task.Inputs as IEnumerable<KeyValuePair<string, string>> ?? new KeyValuePair<string, string>[0]))
                {
                    string key = input.Key?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(key))
                    {
                        if (AgentKnobs.DisableInputTrimming.GetValue(ExecutionContext).AsBoolean())
                        {
                            inputs[key] = input.Value ?? string.Empty;
                        }
                        else
                        {
                            inputs[key] = input.Value?.Trim() ?? string.Empty;
                        }
                    }
                }

                // Expand the inputs.
                Trace.Verbose("Expanding inputs.");
                runtimeVariables.ExpandValues(target: inputs);

                // We need to verify inputs of the tasks that were injected by decorators, to check if they contain secrets,
                // for security reasons execution of tasks in this case should be skipped.
                // Target task inputs could be injected into the decorator's tasks if the decorator has post-task-tasks or pre-task-tasks targets,
                // such tasks will have names that start with __system_pretargettask_ or __system_posttargettask_.
                var taskDecoratorManager = HostContext.GetService<ITaskDecoratorManager>();
                if (taskDecoratorManager.IsInjectedTaskForTarget(Task.Name, ExecutionContext) &&
                    taskDecoratorManager.IsInjectedInputsContainsSecrets(inputs, out var inputsWithSecrets))
                {
                    var inputsForReport = taskDecoratorManager.GenerateTaskResultMessage(inputsWithSecrets);

                    ExecutionContext.Result = TaskResult.Skipped;
                    ExecutionContext.ResultCode = StringUtil.Loc("SecretsAreNotAllowedInInjectedTaskInputs", inputsForReport);
                    return;
                }

                VarUtil.ExpandEnvironmentVariables(HostContext, target: inputs);

                // Translate the server file path inputs to local paths.
                foreach (var input in definition.Data?.Inputs ?? new TaskInputDefinition[0])
                {
                    if (string.Equals(input.InputType, TaskInputType.FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        Trace.Verbose($"Translating file path input '{input.Name}': '{inputs[input.Name]}'");
                        inputs[input.Name] = stepHost.ResolvePathForStepHost(TranslateFilePathInput(inputs[input.Name] ?? string.Empty));
                        Trace.Verbose($"Translated file path input '{input.Name}': '{inputs[input.Name]}'");
                    }
                }

                // Load the task environment.
                Trace.Verbose("Loading task environment.");
                var environment = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer);
                foreach (var env in (Task.Environment ?? new Dictionary<string, string>(0)))
                {
                    string key = env.Key?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(key))
                    {
                        environment[key] = env.Value?.Trim() ?? string.Empty;
                    }
                }

                // Expand the inputs.
                Trace.Verbose("Expanding task environment.");
                runtimeVariables.ExpandValues(target: environment);
                VarUtil.ExpandEnvironmentVariables(HostContext, target: environment);

                // Expand the handler inputs.
                Trace.Verbose("Expanding handler inputs.");
                VarUtil.ExpandValues(HostContext, source: inputs, target: handlerData.Inputs);
                runtimeVariables.ExpandValues(target: handlerData.Inputs);

                // Get each endpoint ID referenced by the task.
                var endpointIds = new List<Guid>();
                foreach (var input in definition.Data?.Inputs ?? new TaskInputDefinition[0])
                {
                    if ((input.InputType ?? string.Empty).StartsWith("connectedService:", StringComparison.OrdinalIgnoreCase))
                    {
                        string inputKey = input?.Name?.Trim() ?? string.Empty;
                        string inputValue;
                        if (!string.IsNullOrEmpty(inputKey) &&
                            inputs.TryGetValue(inputKey, out inputValue) &&
                            !string.IsNullOrEmpty(inputValue))
                        {
                            foreach (string rawId in inputValue.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                Guid parsedId;
                                if (Guid.TryParse(rawId.Trim(), out parsedId) && parsedId != Guid.Empty)
                                {
                                    endpointIds.Add(parsedId);
                                }
                            }
                        }
                    }
                }

                if (endpointIds.Count > 0 &&
                    (runtimeVariables.GetBoolean(WellKnownDistributedTaskVariables.RestrictSecrets) ?? false) &&
                    (runtimeVariables.GetBoolean(Microsoft.TeamFoundation.Build.WebApi.BuildVariables.IsFork) ?? false))
                {
                    ExecutionContext.Result = TaskResult.Skipped;
                    ExecutionContext.ResultCode = $"References service endpoint. PRs from repository forks are not allowed to access secrets in the pipeline. For more information see https://go.microsoft.com/fwlink/?linkid=862029 ";
                    return;
                }

                // Get the endpoints referenced by the task.
                var endpoints = (ExecutionContext.Endpoints ?? new List<ServiceEndpoint>(0))
                    .Join(inner: endpointIds,
                        outerKeySelector: (ServiceEndpoint endpoint) => endpoint.Id,
                        innerKeySelector: (Guid endpointId) => endpointId,
                        resultSelector: (ServiceEndpoint endpoint, Guid endpointId) => endpoint)
                    .ToList();

                // Add the system endpoint.
                foreach (ServiceEndpoint endpoint in (ExecutionContext.Endpoints ?? new List<ServiceEndpoint>(0)))
                {
                    if (string.Equals(endpoint.Name, WellKnownServiceEndpointNames.SystemVssConnection, StringComparison.OrdinalIgnoreCase))
                    {
                        endpoints.Add(endpoint);
                        break;
                    }
                }

                // Get each secure file ID referenced by the task.
                var secureFileIds = new List<Guid>();
                foreach (var input in definition.Data?.Inputs ?? new TaskInputDefinition[0])
                {
                    if (string.Equals(input.InputType ?? string.Empty, "secureFile", StringComparison.OrdinalIgnoreCase))
                    {
                        string inputKey = input?.Name?.Trim() ?? string.Empty;
                        string inputValue;
                        if (!string.IsNullOrEmpty(inputKey) &&
                            inputs.TryGetValue(inputKey, out inputValue) &&
                            !string.IsNullOrEmpty(inputValue))
                        {
                            foreach (string rawId in inputValue.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                Guid parsedId;
                                if (Guid.TryParse(rawId.Trim(), out parsedId) && parsedId != Guid.Empty)
                                {
                                    secureFileIds.Add(parsedId);
                                }
                            }
                        }
                    }
                }

                if (secureFileIds.Count > 0 &&
                    (runtimeVariables.GetBoolean(WellKnownDistributedTaskVariables.RestrictSecrets) ?? false) &&
                    (runtimeVariables.GetBoolean(Microsoft.TeamFoundation.Build.WebApi.BuildVariables.IsFork) ?? false))
                {
                    ExecutionContext.Result = TaskResult.Skipped;
                    ExecutionContext.ResultCode = $"References secure file. PRs from repository forks are not allowed to access secrets in the pipeline. For more information see https://go.microsoft.com/fwlink/?linkid=862029";
                    return;
                }

                // Get the endpoints referenced by the task.
                var secureFiles = (ExecutionContext.SecureFiles ?? new List<SecureFile>(0))
                    .Join(inner: secureFileIds,
                        outerKeySelector: (SecureFile secureFile) => secureFile.Id,
                        innerKeySelector: (Guid secureFileId) => secureFileId,
                        resultSelector: (SecureFile secureFile, Guid secureFileId) => secureFile)
                    .ToList();

                // Set output variables.
                foreach (var outputVar in definition.Data?.OutputVariables ?? new OutputVariable[0])
                {
                    if (outputVar != null && !string.IsNullOrEmpty(outputVar.Name))
                    {
                        ExecutionContext.OutputVariables.Add(outputVar.Name);
                    }
                }

                // translate inputs
                inputs = inputs.ToDictionary(kvp => kvp.Key, kvp => ExecutionContext.TranslatePathForStepTarget(kvp.Value));

                // Create the handler.
                IHandler handler = handlerFactory.Create(
                    ExecutionContext,
                    Task.Reference,
                    stepHost,
                    endpoints,
                    secureFiles,
                    handlerData,
                    inputs,
                    environment,
                    runtimeVariables,
                    taskDirectory: definition.Directory);

                if (AgentKnobs.EnableIssueSourceValidation.GetValue(ExecutionContext).AsBoolean())
                {
                    if (Task.IsServerOwned.HasValue && Task.IsServerOwned.Value && IsCorrelationIdRequired(handler, definition))
                    {
                        environment[Constants.CommandCorrelationIdEnvVar] = ExecutionContext.JobSettings[WellKnownJobSettings.CommandCorrelationId];
                    }
                }

                var enableResourceUtilizationWarnings = AgentKnobs.EnableResourceUtilizationWarnings.GetValue(ExecutionContext).AsBoolean()
                    && !AgentKnobs.DisableResourceUtilizationWarnings.GetValue(ExecutionContext).AsBoolean(); ;

                //Start Resource utility monitors
                IResourceMetricsManager resourceDiagnosticManager = null;

                resourceDiagnosticManager = HostContext.GetService<IResourceMetricsManager>();
                resourceDiagnosticManager.Setup(ExecutionContext);

                if (enableResourceUtilizationWarnings)
                {
                    _ = resourceDiagnosticManager.RunMemoryUtilizationMonitor();
                    _ = resourceDiagnosticManager.RunDiskSpaceUtilizationMonitor();
                    _ = resourceDiagnosticManager.RunCpuUtilizationMonitor(Task.Reference.Id.ToString());
                }
                else
                {
                    ExecutionContext.Debug(StringUtil.Loc("ResourceUtilizationWarningsIsDisabled"));
                }

                // Run the task.
                int retryCount = this.Task.RetryCountOnTaskFailure;

                if (retryCount > 0)
                {
                    if (retryCount > RetryCountOnTaskFailureLimit)
                    {
                        ExecutionContext.Warning(StringUtil.Loc("RetryCountLimitExceeded", RetryCountOnTaskFailureLimit, retryCount));
                        retryCount = RetryCountOnTaskFailureLimit;
                    }

                    RetryHelper rh = new RetryHelper(ExecutionContext, retryCount);
                    await rh.RetryStep(async () => await handler.RunAsync(), RetryHelper.ExponentialDelay);
                }
                else
                {
                    await handler.RunAsync();
                }

                if (enableResourceUtilizationWarnings)
                {
                    resourceDiagnosticManager?.Dispose();
                }
            }
        }

        public async Task VerifyTask(ITaskManager taskManager, Definition definition)
        {
            // Verify task signatures if a fingerprint is configured for the Agent.
            var configurationStore = HostContext.GetService<IConfigurationStore>();
            AgentSettings settings = configurationStore.GetSettings();
            SignatureVerificationMode verificationMode = SignatureVerificationMode.None;
            if (settings.SignatureVerification != null)
            {
                verificationMode = settings.SignatureVerification.Mode;
            }

            if (verificationMode != SignatureVerificationMode.None)
            {
                ISignatureService signatureService = HostContext.CreateService<ISignatureService>();
                Boolean verificationSuccessful = await signatureService.VerifyAsync(definition, ExecutionContext.CancellationToken);

                if (verificationSuccessful)
                {
                    ExecutionContext.Output(StringUtil.Loc("TaskSignatureVerificationSucceeeded"));

                    // Only extract if it's not the checkout task.
                    if (!String.IsNullOrEmpty(definition.ZipPath))
                    {
                        taskManager.Extract(ExecutionContext, Task);
                    }
                }
                else
                {
                    String message = StringUtil.Loc("TaskSignatureVerificationFailed");

                    if (verificationMode == SignatureVerificationMode.Error)
                    {
                        throw new InvalidOperationException(message);
                    }
                    else
                    {
                        ExecutionContext.Warning(message);
                    }
                }
            }
            else if (settings.AlwaysExtractTask)
            {
                // Only extract if it's not the checkout task.
                if (!String.IsNullOrEmpty(definition.ZipPath))
                {
                    taskManager.Extract(ExecutionContext, Task);
                }
            }
        }

        public HandlerData GetHandlerData(IExecutionContext ExecutionContext, ExecutionData currentExecution, PlatformUtil.OS hostOS)
        {
            ArgUtil.NotNull(ExecutionContext, nameof(ExecutionContext));
            if (currentExecution == null)
            {
                return null;
            }

            if ((currentExecution.All.Any(x => x is PowerShell3HandlerData)) &&
                (currentExecution.All.Any(x => x is PowerShellHandlerData && x.Platforms != null && x.Platforms.Contains("windows", StringComparer.OrdinalIgnoreCase))))
            {
                // When task contains both PS and PS3 implementations, we will always prefer PS3 over PS regardless of the platform pinning.
                Trace.Info("Ignore platform pinning for legacy PowerShell execution handler.");
                var legacyPShandler = currentExecution.All.Where(x => x is PowerShellHandlerData).FirstOrDefault();
                legacyPShandler.Platforms = null;
            }

            var targetOS = hostOS;

            var stepTarget = ExecutionContext.StepTarget();
            var preferPowershellHandler = true;
            if (!AgentKnobs.PreferPowershellHandlerOnContainers.GetValue(ExecutionContext).AsBoolean() && stepTarget != null)
            {
                targetOS = stepTarget.ExecutionOS;
                if (stepTarget is ContainerInfo)
                {
                    if ((currentExecution.All.Any(x => x is PowerShell3HandlerData)) &&
                        (currentExecution.All.Any(x => x is BaseNodeHandlerData)))
                    {
                        Trace.Info($"Since we are targeting a container, we will prefer a node handler if one is available");
                        preferPowershellHandler = false;
                    }
                }
            }
            Trace.Info($"Get handler data for target platform {targetOS.ToString()}");
            return currentExecution.All
                    .OrderBy(x => !(x.PreferredOnPlatform(targetOS) && (preferPowershellHandler || !(x is PowerShell3HandlerData)))) // Sort true to false.
                    .ThenBy(x => x.Priority)
                    .FirstOrDefault();
        }

        private string TranslateFilePathInput(string inputValue)
        {
            Trace.Entering();

            if (PlatformUtil.RunningOnWindows && !string.IsNullOrEmpty(inputValue))
            {
                Trace.Verbose("Trim double quotes around filepath type input on Windows.");
                inputValue = inputValue.Trim('\"');

                Trace.Verbose($"Replace any '{Path.AltDirectorySeparatorChar}' with '{Path.DirectorySeparatorChar}'.");
                inputValue = inputValue.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }
            // if inputValue is rooted, return full path.
            string fullPath;
            if (!string.IsNullOrEmpty(inputValue) &&
                inputValue.IndexOfAny(Path.GetInvalidPathChars()) < 0 &&
                Path.IsPathRooted(inputValue))
            {
                try
                {
                    fullPath = Path.GetFullPath(inputValue);
                    Trace.Info($"The original input is a rooted path, return absolute path: {fullPath}");
                    return fullPath;
                }
                catch (Exception ex)
                {
                    Trace.Error(ex);
                    Trace.Info($"The original input is a rooted path, but it is not full qualified, return the path: {inputValue}");
                    return inputValue;
                }
            }

            // use jobextension solve inputValue, if solved result is rooted, return full path.
            var extensionManager = HostContext.GetService<IExtensionManager>();
            IJobExtension[] extensions =
                (extensionManager.GetExtensions<IJobExtension>() ?? new List<IJobExtension>())
                .Where(x => x.HostType.HasFlag(ExecutionContext.Variables.System_HostType))
                .ToArray();
            foreach (IJobExtension extension in extensions)
            {
                fullPath = extension.GetRootedPath(ExecutionContext, inputValue);
                if (!string.IsNullOrEmpty(fullPath))
                {
                    // Stop on the first path root found.
                    Trace.Info($"{extension.HostType.ToString()} JobExtension resolved a rooted path:: {fullPath}");
                    return fullPath;
                }
            }

            // return original inputValue.
            Trace.Info("Cannot root path even by using JobExtension, return original input.");
            return inputValue;
        }

        private bool IsTelemetryPublishRequired()
        {
            // Publish if this is a server owned task or a task we want to track.
            return !Task.IsServerOwned.HasValue ||
                   (Task.IsServerOwned.HasValue && Task.IsServerOwned.Value) ||
                    WellKnownTasks.RequiredForTelemetry.Contains(Task.Reference.Id);
        }

        private void PrintTaskMetaData(Definition taskDefinition)
        {
            ArgUtil.NotNull(Task, nameof(Task));
            ArgUtil.NotNull(Task.Reference, nameof(Task.Reference));
            ArgUtil.NotNull(taskDefinition.Data, nameof(taskDefinition.Data));

            ExecutionContext.Output("==============================================================================", false);
            ExecutionContext.Output($"Task         : {taskDefinition.Data.FriendlyName}", false);
            ExecutionContext.Output($"Description  : {taskDefinition.Data.Description}", false);
            ExecutionContext.Output($"Version      : {Task.Reference.Version}", false);
            ExecutionContext.Output($"Author       : {taskDefinition.Data.Author}", false);
            ExecutionContext.Output($"Help         : {taskDefinition.Data.HelpUrl ?? taskDefinition.Data.HelpMarkDown}", false);
            ExecutionContext.Output("==============================================================================", false);
        }

        private void PublishTelemetry(Definition taskDefinition, HandlerData handlerData)
        {
            if (!IsTelemetryPublishRequired())
            {
                return;
            }

            ArgUtil.NotNull(Task, nameof(Task));
            ArgUtil.NotNull(Task.Reference, nameof(Task.Reference));
            ArgUtil.NotNull(taskDefinition.Data, nameof(taskDefinition.Data));

            try
            {
                var useNode10 = AgentKnobs.UseNode10.GetValue(ExecutionContext).AsString();
                var expectedExecutionHandler = (taskDefinition.Data.Execution?.All != null) ? string.Join(", ", taskDefinition.Data.Execution.All) : "";
                var systemVersion = PlatformUtil.GetSystemVersion();

                Dictionary<string, string> telemetryData = new Dictionary<string, string>
                {
                    { "TaskName", Task.Reference.Name },
                    { "TaskId", Task.Reference.Id.ToString() },
                    { "Version", Task.Reference.Version },
                    { "OS", PlatformUtil.GetSystemId() ?? "" },
                    { "OSVersion", systemVersion?.Name?.ToString() ?? "" },
                    { "OSBuild", systemVersion?.Version?.ToString() ?? "" },
                    { "ExpectedExecutionHandler", expectedExecutionHandler },
                    { "RealExecutionHandler", handlerData.ToString() },
                    { "UseNode10", useNode10 },
                    { "JobId", ExecutionContext.Variables.System_JobId.ToString()},
                    { "PlanId", ExecutionContext.Variables.Get(Constants.Variables.System.PlanId)},
                    { "AgentName", ExecutionContext.Variables.Get(Constants.Variables.Agent.Name)},
                    { "MachineName", ExecutionContext.Variables.Get(Constants.Variables.Agent.MachineName)},
                    { "IsSelfHosted", ExecutionContext.Variables.Get(Constants.Variables.Agent.IsSelfHosted)},
                    { "IsAzureVM", ExecutionContext.Variables.Get(Constants.Variables.System.IsAzureVM)},
                    { "IsDockerContainer", ExecutionContext.Variables.Get(Constants.Variables.System.IsDockerContainer)}
                };

                var cmd = new Command("telemetry", "publish");
                cmd.Data = JsonConvert.SerializeObject(telemetryData, Formatting.None);
                cmd.Properties.Add("area", "PipelinesTasks");
                cmd.Properties.Add("feature", "ExecutionHandler");

                var publishTelemetryCmd = new TelemetryCommandExtension();
                publishTelemetryCmd.Initialize(HostContext);
                publishTelemetryCmd.ProcessCommand(ExecutionContext, cmd);
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentNullException || ex is NullReferenceException)
            {
                ExecutionContext.Debug($"ExecutionHandler telemetry wasn't published, because one of the variables has unexpected value.");
                ExecutionContext.Debug(ex.ToString());
            }
        }

        private bool IsCorrelationIdRequired(IHandler handler, Definition task)
        {
            Trace.Entering();
            var isIdRequired = false;

            if (handler is INodeHandler)
            {
                Trace.Info("Current handler is Node. Trying to determing the SDK version.");
                var nodeSdkVer = task.GetNodeSDKVersion();

                if (nodeSdkVer == null)
                {
                    Trace.Error("Unable to determine the Node SDK version.");
                }
                else
                {
                    var minVer = new TaskVersion() { Major = 4, Minor = 10, Patch = 1 };
                    isIdRequired = (nodeSdkVer >= minVer) && !((nodeSdkVer.Major == 5) && nodeSdkVer.IsTest);
                    Trace.Info($"Node SDK version: {nodeSdkVer}. Correlation ID is required: {isIdRequired}.");
                }
            }
            else if (handler is IPowerShell3Handler)
            {
                Trace.Info("Current handler is PowerShell3. Trying to determing the SDK version.");
                var psSdkVer = task.GetPowerShellSDKVersion();
                if (psSdkVer == null)
                {
                    Trace.Error("Unable to determine the PowerShell SDK version.");
                }
                else
                {
                    var minVer = new TaskVersion() { Major = 0, Minor = 20, Patch = 1 };
                    isIdRequired = psSdkVer >= minVer;
                    Trace.Info($"PowerShell SDK version: {psSdkVer}. Correlation ID is required: {isIdRequired}.");
                }
            }
            else
            {
                Trace.Info($"Current handler is {handler.GetType()}. Correlation ID is not allowed.");
            }

            Trace.Leaving();
            return isIdRequired;
        }
    }
}
