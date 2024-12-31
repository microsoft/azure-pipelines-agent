// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Agent.Sdk;
using Agent.Sdk.Knob;

using Microsoft.TeamFoundation.DistributedTask.Expressions;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;

using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

using Newtonsoft.Json;
using Microsoft.VisualStudio.Services.Agent.Worker.Telemetry;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    public interface IStep
    {
        IExpressionNode Condition { get; set; }
        bool ContinueOnError { get; }
        string DisplayName { get; }
        Pipelines.StepTarget Target { get; }
        bool Enabled { get; }
        IExecutionContext ExecutionContext { get; set; }
        TimeSpan? Timeout { get; }
        Task RunAsync();
    }

    [ServiceLocator(Default = typeof(StepsRunner))]
    public interface IStepsRunner : IAgentService
    {
        Task RunAsync(IExecutionContext Context, IList<IStep> steps);
    }

    public sealed class StepsRunner : AgentService, IStepsRunner
    {
        // StepsRunner should never throw exception to caller
        public async Task RunAsync(IExecutionContext jobContext, IList<IStep> steps)
        {
            ArgUtil.NotNull(jobContext, nameof(jobContext));
            ArgUtil.NotNull(steps, nameof(steps));

            // TaskResult:
            //  Abandoned (Server set this.)
            //  Canceled
            //  Failed
            //  Skipped
            //  Succeeded
            //  SucceededWithIssues
            CancellationTokenRegistration? jobCancelRegister = null;
            int stepIndex = 0;
            jobContext.Variables.Agent_JobStatus = jobContext.Result ?? TaskResult.Succeeded;
            // Wait till all async commands finish.
            foreach (var command in jobContext.AsyncCommands ?? new List<IAsyncCommandContext>())
            {
                try
                {
                    // wait async command to finish.
                    await command.WaitAsync();
                }

                catch (Exception ex)
                {
                    // Log the error
                    Trace.Info($"Caught exception from async command {command.Name}: {ex}");
                }
            }
            foreach (IStep step in steps)
            {
                Trace.Info($"Processing step: DisplayName='{step.DisplayName}', ContinueOnError={step.ContinueOnError}, Enabled={step.Enabled}");
                ArgUtil.Equal(true, step.Enabled, nameof(step.Enabled));
                ArgUtil.NotNull(step.ExecutionContext, nameof(step.ExecutionContext));
                ArgUtil.NotNull(step.ExecutionContext.Variables, nameof(step.ExecutionContext.Variables));
                stepIndex++;

                // Start.
                step.ExecutionContext.Start();
                var taskStep = step as ITaskRunner;
                if (taskStep != null)
                {
                    HostContext.WritePerfCounter($"TaskStart_{taskStep.Task.Reference.Name}_{stepIndex}");
                }

                // Change the current job context to the step context.
                // var resourceDiagnosticManager = HostContext.GetService<IResourceMetricsManager>();
                // resourceDiagnosticManager.SetContext(step.ExecutionContext);

                // Variable expansion.
                step.ExecutionContext.SetStepTarget(step.Target);
                List<string> expansionWarnings;
                step.ExecutionContext.Variables.RecalculateExpanded(out expansionWarnings);
                expansionWarnings?.ForEach(x => step.ExecutionContext.Warning(x));

                var expressionManager = HostContext.GetService<IExpressionManager>();
                try
                {
                    ArgUtil.NotNull(jobContext, nameof(jobContext)); // I am not sure why this is needed, but static analysis flagged all uses of jobContext below this point
                    // Register job cancellation call back only if job cancellation token not been fire before each step run
                    if (!jobContext.CancellationToken.IsCancellationRequested)
                    {
                        // Test the condition again. The job was canceled after the condition was originally evaluated.
                        jobCancelRegister = jobContext.CancellationToken.Register(() =>
                        {
                            // mark job as cancelled
                            jobContext.Result = TaskResult.Canceled;
                            jobContext.Variables.Agent_JobStatus = jobContext.Result;

                            step.ExecutionContext.Debug($"Re-evaluate condition on job cancellation for step: '{step.DisplayName}'.");
                            ConditionResult conditionReTestResult;
                            if (HostContext.AgentShutdownToken.IsCancellationRequested)
                            {
                                if (AgentKnobs.FailJobWhenAgentDies.GetValue(jobContext).AsBoolean())
                                {
                                    PublishTelemetry(jobContext, TaskResult.Failed.ToString(), "120");
                                    jobContext.Result = TaskResult.Failed;
                                    jobContext.Variables.Agent_JobStatus = jobContext.Result;
                                }
                                step.ExecutionContext.Debug($"Skip Re-evaluate condition on agent shutdown.");
                                conditionReTestResult = false;
                            }
                            else
                            {
                                try
                                {
                                    conditionReTestResult = expressionManager.Evaluate(step.ExecutionContext, step.Condition, hostTracingOnly: true);
                                }
                                catch (Exception ex)
                                {
                                    // Cancel the step since we get exception while re-evaluate step condition.
                                    Trace.Info("Caught exception from expression when re-test condition on job cancellation.");
                                    step.ExecutionContext.Error(ex);
                                    conditionReTestResult = false;
                                }
                            }

                            if (!conditionReTestResult.Value)
                            {
                                // Cancel the step.
                                Trace.Info("Cancel current running step.");
                                step.ExecutionContext.Error(StringUtil.Loc("StepCancelled"));
                                step.ExecutionContext.CancelToken();
                            }
                        });
                    }
                    else if (AgentKnobs.FailJobWhenAgentDies.GetValue(jobContext).AsBoolean() &&
                            HostContext.AgentShutdownToken.IsCancellationRequested)
                    {
                        if (jobContext.Result != TaskResult.Failed)
                        {
                            // mark job as failed
                            PublishTelemetry(jobContext, jobContext.Result.ToString(), "121");
                            jobContext.Result = TaskResult.Failed;
                            jobContext.Variables.Agent_JobStatus = jobContext.Result;
                        }
                    }
                    else
                    {
                        if (jobContext.Result != TaskResult.Canceled)
                        {
                            // mark job as cancelled
                            jobContext.Result = TaskResult.Canceled;
                            jobContext.Variables.Agent_JobStatus = jobContext.Result;
                        }
                    }

                    // Evaluate condition.
                    step.ExecutionContext.Debug($"Evaluating condition for step: '{step.DisplayName}'");
                    Exception conditionEvaluateError = null;
                    ConditionResult conditionResult;
                    if (HostContext.AgentShutdownToken.IsCancellationRequested)
                    {
                        step.ExecutionContext.Debug($"Skip evaluate condition on agent shutdown.");
                        conditionResult = false;
                    }
                    else
                    {
                        try
                        {
                            conditionResult = expressionManager.Evaluate(step.ExecutionContext, step.Condition);
                        }
                        catch (Exception ex)
                        {
                            Trace.Info("Caught exception from expression.");
                            Trace.Error(ex);
                            conditionResult = false;
                            conditionEvaluateError = ex;
                        }
                    }

                    // no evaluate error but condition is false
                    if (!conditionResult.Value && conditionEvaluateError == null)
                    {
                        // Condition == false
                        string skipStepMessage = "Skipping step due to condition evaluation.";
                        Trace.Info(skipStepMessage);
                        step.ExecutionContext.Output($"{skipStepMessage}\n{conditionResult.Trace}");
                        step.ExecutionContext.Complete(TaskResult.Skipped, resultCode: skipStepMessage);
                        continue;
                    }

                    if (conditionEvaluateError != null)
                    {
                        // fail the step since there is an evaluate error.
                        step.ExecutionContext.Error(conditionEvaluateError);
                        step.ExecutionContext.Complete(TaskResult.Failed);
                    }
                    else
                    {
                        // Run the step.
                        Trace.Info("##DEBUG_SB: Running the step in StepsRunner.");
                        await RunStepAsync(step, jobContext.CancellationToken);
                        Trace.Info($"##DEBUG_SB: Step result: {step.ExecutionContext.Result}");
                        Trace.Info("##DEBUG_SB: Finished running the step in StepsRunner.");
                    }
                }
                finally
                {
                    if (jobCancelRegister != null)
                    {
                        jobCancelRegister?.Dispose();
                        jobCancelRegister = null;
                    }
                }

                // Update the job result.
                if (step.ExecutionContext.Result == TaskResult.SucceededWithIssues ||
                    step.ExecutionContext.Result == TaskResult.Failed)
                {
                    Trace.Info($"Update job result with current step result '{step.ExecutionContext.Result}'.");
                    jobContext.Result = TaskResultUtil.MergeTaskResults(jobContext.Result, step.ExecutionContext.Result.Value);
                    jobContext.Variables.Agent_JobStatus = jobContext.Result;
                }
                else
                {
                    Trace.Info($"No need for updating job result with current step result '{step.ExecutionContext.Result}'.");
                }

                if (taskStep != null)
                {
                    HostContext.WritePerfCounter($"TaskCompleted_{taskStep.Task.Reference.Name}_{stepIndex}");
                }

                Trace.Info($"Current state: job state = '{jobContext.Result}'");
            }
        }

        private async Task RunStepAsync(IStep step, CancellationToken jobCancellationToken)
        {
            // Start the step.
            Trace.Info("Starting the step.");
            step.ExecutionContext.Section(StringUtil.Loc("StepStarting", step.DisplayName));
            step.ExecutionContext.SetTimeout(timeout: step.Timeout);

            step.ExecutionContext.Variables.Set(Constants.Variables.Task.SkipTranslatorForCheckout, Boolean.FalseString);

            // Windows may not be on the UTF8 codepage; try to fix that
            await SwitchToUtf8Codepage(step);

            try
            {
                await step.RunAsync();
            }
            catch (OperationCanceledException ex)
            {
                if (step.ExecutionContext.CancellationToken.IsCancellationRequested &&
                    !jobCancellationToken.IsCancellationRequested)
                {
                    Trace.Error($"Caught timeout exception from step: {ex.Message}");
                    step.ExecutionContext.Error(StringUtil.Loc("StepTimedOut"));
                    step.ExecutionContext.Result = TaskResult.Failed;
                }
                else if (AgentKnobs.FailJobWhenAgentDies.GetValue(step.ExecutionContext).AsBoolean() &&
                        HostContext.AgentShutdownToken.IsCancellationRequested)
                {
                    PublishTelemetry(step.ExecutionContext, TaskResult.Failed.ToString(), "122");
                    Trace.Error($"Caught Agent Shutdown exception from step: {ex.Message}");
                    step.ExecutionContext.Error(ex);
                    step.ExecutionContext.Result = TaskResult.Failed;
                }
                else
                {
                    // Log the exception and cancel the step.
                    Trace.Error($"Caught cancellation exception from step: {ex}");
                    step.ExecutionContext.Error(ex);
                    step.ExecutionContext.Result = TaskResult.Canceled;
                }
            }
            catch (Exception ex)
            {
                // Log the error and fail the step.
                Trace.Error($"Caught exception from step: {ex}");
                step.ExecutionContext.Error(ex);
                step.ExecutionContext.Result = TaskResult.Failed;
            }

            // Wait till all async commands finish.
            foreach (var command in step.ExecutionContext.AsyncCommands ?? new List<IAsyncCommandContext>())
            {
                try
                {
                    // wait async command to finish.
                    await command.WaitAsync();
                }
                catch (OperationCanceledException ex)
                {
                    if (step.ExecutionContext.CancellationToken.IsCancellationRequested &&
                        !jobCancellationToken.IsCancellationRequested)
                    {
                        // Log the timeout error, set step result to falied if the current result is not canceled.
                        Trace.Error($"Caught timeout exception from async command {command.Name}: {ex}");
                        step.ExecutionContext.Error(StringUtil.Loc("StepTimedOut"));

                        // if the step already canceled, don't set it to failed.
                        Trace.Info($"##DEBUG_SB: check 1 step.ExecutionContext.CommandResult: {step.ExecutionContext.CommandResult}");
                        step.ExecutionContext.CommandResult = TaskResultUtil.MergeTaskResults(step.ExecutionContext.CommandResult, TaskResult.Failed);
                    }
                    else if (AgentKnobs.FailJobWhenAgentDies.GetValue(step.ExecutionContext).AsBoolean() &&
                            HostContext.AgentShutdownToken.IsCancellationRequested)
                    {
                        PublishTelemetry(step.ExecutionContext, TaskResult.Failed.ToString(), "123");
                        Trace.Error($"Caught Agent shutdown exception from async command {command.Name}: {ex}");
                        step.ExecutionContext.Error(ex);

                        // if the step already canceled, don't set it to failed.
                        Trace.Info($"##DEBUG_SB: check 2 step.ExecutionContext.CommandResult: {step.ExecutionContext.CommandResult}");
                        step.ExecutionContext.CommandResult = TaskResultUtil.MergeTaskResults(step.ExecutionContext.CommandResult, TaskResult.Failed);
                    }
                    else
                    {
                        // log and save the OperationCanceledException, set step result to canceled if the current result is not failed.
                        Trace.Error($"Caught cancellation exception from async command {command.Name}: {ex}");
                        step.ExecutionContext.Error(ex);

                        // if the step already failed, don't set it to canceled.
                        Trace.Info($"##DEBUG_SB: check 3 step.ExecutionContext.CommandResult: {step.ExecutionContext.CommandResult}");
                        step.ExecutionContext.CommandResult = TaskResultUtil.MergeTaskResults(step.ExecutionContext.CommandResult, TaskResult.Canceled);
                    }
                }
                catch (Exception ex)
                {
                    // Log the error, set step result to falied if the current result is not canceled.
                    Trace.Error($"Caught exception from async command {command.Name}: {ex}");
                    step.ExecutionContext.Error(ex);

                    // if the step already canceled, don't set it to failed.
                    Trace.Info($"##DEBUG_SB: check 4 step.ExecutionContext.CommandResult: {step.ExecutionContext.CommandResult}");
                    step.ExecutionContext.CommandResult = TaskResultUtil.MergeTaskResults(step.ExecutionContext.CommandResult, TaskResult.Failed);
                }
            }

            // Merge executioncontext result with command result
            if (step.ExecutionContext.CommandResult != null)
            {
                Trace.Info($"##DEBUG_SB: Merging step result with command result: {step.ExecutionContext.CommandResult.Value}");
                Trace.Info($"##DEBUG_SB: Before merge, step result: {step.ExecutionContext.Result}");
                step.ExecutionContext.Result = TaskResultUtil.MergeTaskResults(step.ExecutionContext.Result, step.ExecutionContext.CommandResult.Value);
            }

            // Fixup the step result if ContinueOnError.
            if (step.ExecutionContext.Result == TaskResult.Failed && step.ContinueOnError)
            {
                step.ExecutionContext.Result = TaskResult.SucceededWithIssues;
                Trace.Info($"Updated step result: {step.ExecutionContext.Result}");
            }
            else
            {
                // display status of TaskResult
                try
                {
                    Trace.Info($"##DEBUG_SB: TaskResult.Succeeded: {TaskResult.Succeeded}");
                }
                catch (Exception ex)
                {
                    Trace.Info($"##DEBUG_SB: Could not display TaskResult.Succeeded: {ex.Message}");
                }
                try
                {
                    Trace.Info($"##DEBUG_SB: TaskResult.SucceededWithIssues: {TaskResult.SucceededWithIssues}");
                }
                catch (Exception ex)
                {
                    Trace.Info($"##DEBUG_SB: Could not display TaskResult.SucceededWithIssues: {ex.Message}");
                }
                try
                {
                    Trace.Info($"##DEBUG_SB: TaskResult.Failed: {TaskResult.Failed}");
                }
                catch (Exception ex)
                {
                    Trace.Info($"##DEBUG_SB: Could not display TaskResult.Failed: {ex.Message}");
                }
                try
                {
                    Trace.Info($"##DEBUG_SB: TaskResult.Canceled: {TaskResult.Canceled}");
                }
                catch (Exception ex)
                {
                    Trace.Info($"##DEBUG_SB: Could not display TaskResult.Canceled: {ex.Message}");
                }
                try
                {
                    Trace.Info($"##DEBUG_SB: TaskResult.Skipped: {TaskResult.Skipped}");
                }
                catch (Exception ex)
                {
                    Trace.Info($"##DEBUG_SB: Could not display TaskResult.Skipped: {ex.Message}");
                }
                try
                {
                    Trace.Info($"##DEBUG_SB: TaskResult.Abandoned: {TaskResult.Abandoned}");
                }
                catch (Exception ex)
                {
                    Trace.Info($"##DEBUG_SB: Could not display TaskResult.Abandoned: {ex.Message}");
                }
                
                
                Trace.Info($"Step result: {step.ExecutionContext.Result}");
            }

            // Complete the step context.
            Trace.Info("##DEBUG_SB: Finishing step in RunStepAsync.");
            Trace.Info($"##DEBUG_SB: check1 Step result: {step.ExecutionContext.Result}");
            step.ExecutionContext.Section(StringUtil.Loc("StepFinishing", step.DisplayName));
            Trace.Info($"##DEBUG_SB: check2 Step result: {step.ExecutionContext.Result}");
            step.ExecutionContext.Complete();
            Trace.Info($"##DEBUG_SB: check3 Step result: {step.ExecutionContext.Result}");
        }

        private async Task SwitchToUtf8Codepage(IStep step)
        {
            if (!PlatformUtil.RunningOnWindows)
            {
                return;
            }

            try
            {
                if (step.ExecutionContext.Variables.Retain_Default_Encoding != true && Console.InputEncoding.CodePage != 65001)
                {
                    using var pi = HostContext.CreateService<IProcessInvoker>();

                    using var timeoutTokenSource = new CancellationTokenSource();
                    // 1 minute should be enough to switch to UTF8 code page
                    timeoutTokenSource.CancelAfter(TimeSpan.FromSeconds(60));

                    // Join main and timeout cancellation tokens
                    using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                        step.ExecutionContext.CancellationToken,
                        timeoutTokenSource.Token);

                    try
                    {
                        // Use UTF8 code page
                        int exitCode = await pi.ExecuteAsync(workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                                                fileName: WhichUtil.Which("chcp", true, Trace),
                                                arguments: "65001",
                                                environment: null,
                                                requireExitCodeZero: false,
                                                outputEncoding: null,
                                                killProcessOnCancel: false,
                                                redirectStandardIn: null,
                                                inheritConsoleHandler: true,
                                                continueAfterCancelProcessTreeKillAttempt: ProcessInvoker.ContinueAfterCancelProcessTreeKillAttemptDefault,
                                                cancellationToken: linkedTokenSource.Token);
                        if (exitCode == 0)
                        {
                            Trace.Info("Successfully returned to code page 65001 (UTF8)");
                        }
                        else
                        {
                            Trace.Warning($"'chcp 65001' failed with exit code {exitCode}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        if (!timeoutTokenSource.IsCancellationRequested)
                        {
                            throw;
                        }

                        Trace.Warning("'chcp 65001' cancelled by timeout");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.Warning($"'chcp 65001' failed with exception {ex.Message}");
            }
        }

        private void PublishTelemetry(IExecutionContext context, string Task_Result, string TracePoint)
        {
            try
            {
                var telemetryData = new Dictionary<string, string>
                {
                    { "JobId", context.Variables.System_JobId.ToString()},
                    { "JobResult", Task_Result },
                    { "TracePoint", TracePoint},
                };
                var cmd = new Command("telemetry", "publish");
                cmd.Data = JsonConvert.SerializeObject(telemetryData, Formatting.None);
                cmd.Properties.Add("area", "PipelinesTasks");
                cmd.Properties.Add("feature", "AgentShutdown");

                var publishTelemetryCmd = new TelemetryCommandExtension();
                publishTelemetryCmd.Initialize(HostContext);
                publishTelemetryCmd.ProcessCommand(context, cmd);
            }
            catch (Exception ex)
            {
                Trace.Warning($"Unable to publish agent shutdown telemetry data. Exception: {ex}");
            }
        }
    }
}
