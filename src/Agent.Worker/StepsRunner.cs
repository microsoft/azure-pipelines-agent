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
            Trace.Entering();

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
            Trace.Info($"Async command completion wait initiated - processing {jobContext.AsyncCommands?.Count ?? 0} pending commands");
            // Wait till all async commands finish.
            int successfulCommandCount = 0;
            foreach (var command in jobContext.AsyncCommands ?? new List<IAsyncCommandContext>())
            {
                try
                {
                    // wait async command to finish.
                    Trace.Info($"Async command initiated [Command:{command.Name}, CommandType:{command.GetType().Name}]");
                    await command.WaitAsync();
                    successfulCommandCount++;
                    Trace.Info($"Async command completed successfully: {command.Name}");
                }

                catch (Exception ex)
                {
                    // Log the error
                    Trace.Info($"Async command failed during job initialization [Command:{command.Name}, JobId:{jobContext.Variables.System_JobId}, Error:{ex.Message}]");
                }
            }
            Trace.Info($"Async command completion wait finished - {successfulCommandCount} commands processed");
            Trace.Info("Step iteration loop initiated - beginning sequential step processing");
            foreach (IStep step in steps)
            {
                Trace.Info($"Processing step {stepIndex + 1}/{steps.Count}: DisplayName='{step.DisplayName}', ContinueOnError={step.ContinueOnError}, Enabled={step.Enabled}");
                ArgUtil.Equal(true, step.Enabled, nameof(step.Enabled));
                ArgUtil.NotNull(step.ExecutionContext, nameof(step.ExecutionContext));
                ArgUtil.NotNull(step.ExecutionContext.Variables, nameof(step.ExecutionContext.Variables));
                stepIndex++;

                Trace.Info($"ExecutionContext startup initiated for step: '{step.DisplayName}'");
                // Start.
                step.ExecutionContext.Start();
                var taskStep = step as ITaskRunner;
                if (taskStep != null)
                {
                    HostContext.WritePerfCounter($"TaskStart_{taskStep.Task.Reference.Name}_{stepIndex}");
                    Trace.Info($"Task step initiated [TaskName:{taskStep.Task.Reference.Name}, TaskId:{taskStep.Task.Reference.Id}, Version:{taskStep.Task.Reference.Version}, Stage:{taskStep.Stage}]");
                }
                else
                {
                    Trace.Info($"Non-task step {step.DisplayName} started [StepType:{step.GetType().Name}, Timeout:{step.Timeout?.TotalMinutes ?? 0}min]");
                }

                // Change the current job context to the step context.
                var resourceDiagnosticManager = HostContext.GetService<IResourceMetricsManager>();
                resourceDiagnosticManager.SetContext(step.ExecutionContext);

                // Variable expansion.
                step.ExecutionContext.SetStepTarget(step.Target);
                List<string> expansionWarnings;
                step.ExecutionContext.Variables.RecalculateExpanded(out expansionWarnings);
                expansionWarnings?.ForEach(x => step.ExecutionContext.Warning(x));
                Trace.Info($"Variable expansion completed [Step:'{step.DisplayName}', Warnings:{expansionWarnings?.Count ?? 0}, Target:{step.Target?.GetType()?.Name ?? "None"}]");

                var expressionManager = HostContext.GetService<IExpressionManager>();
                try
                {
                    ArgUtil.NotNull(jobContext, nameof(jobContext)); // I am not sure why this is needed, but static analysis flagged all uses of jobContext below this point
                    // Register job cancellation call back only if job cancellation token not been fire before each step run
                    if (!jobContext.CancellationToken.IsCancellationRequested)
                    {
                        Trace.Info($"Job cancellation registration setup [Step:'{step.DisplayName}', JobCancellationRequested:False, RegistrationActive:True]");
                        // Test the condition again. The job was canceled after the condition was originally evaluated.
                        jobCancelRegister = jobContext.CancellationToken.Register(() =>
                        {
                            Trace.Info($"Job cancellation callback triggered [Step:'{step.DisplayName}', AgentShutdown:{HostContext.AgentShutdownToken.IsCancellationRequested}]");
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
                                    Trace.Info($"Agent shutdown failure applied [Step:'{step.DisplayName}', FailJobEnabled:True, JobResult:Failed]");
                                }
                                step.ExecutionContext.Debug($"Skip Re-evaluate condition on agent shutdown.");
                                conditionReTestResult = false;
                                Trace.Info($"Condition re-evaluation skipped [Step:'{step.DisplayName}', Reason:AgentShutdown]");
                            }
                            else
                            {
                                try
                                {
                                    Trace.Info($"Condition re-evaluation initiated [Step:'{step.DisplayName}', Expression:'{step.Condition}', HostTracingOnly:True]");
                                    conditionReTestResult = expressionManager.Evaluate(step.ExecutionContext, step.Condition, hostTracingOnly: true);
                                    Trace.Info($"Condition re-evaluation completed [Step:'{step.DisplayName}', Result:{conditionReTestResult.Value}]");
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
                                Trace.Info($"Cancel current running step: {step.DisplayName}");
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
                        Trace.Info($"Condition evaluation skipped due to agent shutdown: '{step.DisplayName}'");
                    }
                    else
                    {
                        try
                        {
                            conditionResult = expressionManager.Evaluate(step.ExecutionContext, step.Condition);
                            Trace.Info($"Condition evaluation completed - Result: {conditionResult.Value}, Step: '{step.DisplayName}'");
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
                        Trace.Info($"Step skipped due to condition evaluation [Step: '{step.DisplayName}', Reason:ConditionFalse, Expression:'{step.Condition}', StepIndex:{stepIndex}/{steps.Count}]");
                        step.ExecutionContext.Output($"{skipStepMessage}\n{conditionResult.Trace}");
                        step.ExecutionContext.Complete(TaskResult.Skipped, resultCode: skipStepMessage);
                        continue;
                    }

                    if (conditionEvaluateError != null)
                    {
                        // fail the step since there is an evaluate error.
                        Trace.Error($"Condition evaluation failure context [Step:'{step.DisplayName}', Expression:'{step.Condition}', StepIndex:{stepIndex}/{steps.Count}]");
                        step.ExecutionContext.Error(conditionEvaluateError);
                        step.ExecutionContext.Complete(TaskResult.Failed);
                    }
                    else
                    {
                        Trace.Info($"RunStepAsync execution initiated for step: '{step.DisplayName}'");
                        // Run the step.
                        await RunStepAsync(step, jobContext.CancellationToken);
                        Trace.Info($"RunStepAsync execution completed for step: '{step.DisplayName}' - Result: {step.ExecutionContext.Result}");
                    }
                }
                finally
                {
                    Trace.Info($"Step cancellation registration cleanup [Step:'{step.DisplayName}', RegistrationActive:{jobCancelRegister != null}]");
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
                    Trace.Info($"Update job result with current step result - Step: '{step.DisplayName}', StepResult: {step.ExecutionContext.Result}, PreviousJobResult: {jobContext.Result}");
                    jobContext.Result = TaskResultUtil.MergeTaskResults(jobContext.Result, step.ExecutionContext.Result.Value);
                    jobContext.Variables.Agent_JobStatus = jobContext.Result;
                    Trace.Info($"Job result after merge: {jobContext.Result}");
                }
                else
                {
                    Trace.Info($"Job result unchanged - Step: '{step.DisplayName}', StepResult: {step.ExecutionContext.Result}, JobResultKept:{jobContext.Result}");
                }

                if (taskStep != null)
                {
                    HostContext.WritePerfCounter($"TaskCompleted_{taskStep.Task.Reference.Name}_{stepIndex}");
                    Trace.Info($"Task step completion - TaskName:{taskStep.Task.Reference.Name}, StepIndex:{stepIndex}/{steps.Count}, Result: {step.ExecutionContext.Result}, TaskStage:{taskStep.Stage}");
                }

            }
            Trace.Info($"Step iteration loop completed - All {steps.Count} steps processed, Final job result: {jobContext.Result}");
        }

        private async Task RunStepAsync(IStep step, CancellationToken jobCancellationToken)
        {
            Trace.Info($"Individual step execution initiated: '{step.DisplayName}'");
            // Start the step.

            step.ExecutionContext.Section(StringUtil.Loc("StepStarting", step.DisplayName));
            step.ExecutionContext.SetTimeout(timeout: step.Timeout);

            step.ExecutionContext.Variables.Set(Constants.Variables.Task.SkipTranslatorForCheckout, Boolean.FalseString);

            Trace.Info($"UTF-8 codepage switching initiated for step: '{step.DisplayName}'");
            // Windows may not be on the UTF8 codepage; try to fix that
            await SwitchToUtf8Codepage(step);
            Trace.Info($"UTF-8 codepage switching completed for step: '{step.DisplayName}'");
            // updated code log - Add codepage switching context and platform info
            Trace.Info($"Codepage configuration [Platform:{(PlatformUtil.RunningOnWindows ? "Windows" : "Unix")}, RetainEncoding:{step.ExecutionContext.Variables.Retain_Default_Encoding}, CurrentCodepage:{Console.InputEncoding?.CodePage}]");

            try
            {
                Trace.Info($"Step main execution initiated: '{step.DisplayName}'");
                await step.RunAsync();
                Trace.Info($"Step main execution completed successfully: '{step.DisplayName}'");
            }
            catch (OperationCanceledException ex)
            {
                if (step.ExecutionContext.CancellationToken.IsCancellationRequested &&
                    !jobCancellationToken.IsCancellationRequested)
                {
                    Trace.Error($"Caught timeout exception from step: Step: {step.DisplayName}, Exception: {ex.Message}, ConfiguredTimeout:{step.Timeout?.TotalMinutes ?? 0}min");
                    step.ExecutionContext.Error(StringUtil.Loc("StepTimedOut"));
                    step.ExecutionContext.Result = TaskResult.Failed;
                }
                else if (AgentKnobs.FailJobWhenAgentDies.GetValue(step.ExecutionContext).AsBoolean() &&
                        HostContext.AgentShutdownToken.IsCancellationRequested)
                {
                    PublishTelemetry(step.ExecutionContext, TaskResult.Failed.ToString(), "122");
                    Trace.Error($"Caught Agent Shutdown exception from step: Step:'{step.DisplayName}', ShutdownReason:{HostContext.AgentShutdownReason}, Exception: {ex.Message}");
                    step.ExecutionContext.Error(ex);
                    step.ExecutionContext.Result = TaskResult.Failed;
                }
                else
                {
                    // Log the exception and cancel the step.
                    Trace.Error($"Caught cancellation exception from step: Step:{step.DisplayName}, CancellationSource:JobLevel, JobCancelled:{jobCancellationToken.IsCancellationRequested}");
                    step.ExecutionContext.Error(ex);
                    step.ExecutionContext.Result = TaskResult.Canceled;
                }
            }
            catch (Exception ex)
            {
                Trace.Error($"Caught exception from step: - Step: '{step.DisplayName}', Exception: {ex}");
                // Log the error and fail the step.
                step.ExecutionContext.Error(ex);
                step.ExecutionContext.Result = TaskResult.Failed;
            }

            Trace.Info($"Async command completion wait initiated for step: '{step.DisplayName}' - Commands: {step.ExecutionContext.AsyncCommands?.Count ?? 0}");
            // Wait till all async commands finish.
            foreach (var command in step.ExecutionContext.AsyncCommands ?? new List<IAsyncCommandContext>())
            {
                try
                {
                    // wait async command to finish.
                    // check this - add log to mark start of this call as well, also add required meatadata to log for it
                    Trace.Info($"Step async command initiated [Command:{command.Name}, Step:'{step.DisplayName}', CommandType:{command.GetType().Name}]");
                    await command.WaitAsync();
                    Trace.Info($"Step async command completion [Command:{command.Name}]");
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
                        step.ExecutionContext.CommandResult = TaskResultUtil.MergeTaskResults(step.ExecutionContext.CommandResult, TaskResult.Failed);
                    }
                    else if (AgentKnobs.FailJobWhenAgentDies.GetValue(step.ExecutionContext).AsBoolean() &&
                            HostContext.AgentShutdownToken.IsCancellationRequested)
                    {
                        PublishTelemetry(step.ExecutionContext, TaskResult.Failed.ToString(), "123");
                        Trace.Error($"Caught Agent shutdown exception from async command {command.Name}: {ex}");
                        step.ExecutionContext.Error(ex);

                        // if the step already canceled, don't set it to failed.
                        step.ExecutionContext.CommandResult = TaskResultUtil.MergeTaskResults(step.ExecutionContext.CommandResult, TaskResult.Failed);
                    }
                    else
                    {
                        // log and save the OperationCanceledException, set step result to canceled if the current result is not failed.
                        Trace.Error($"Caught cancellation exception from async command {command.Name}: {ex}");
                        step.ExecutionContext.Error(ex);

                        // if the step already failed, don't set it to canceled.
                        step.ExecutionContext.CommandResult = TaskResultUtil.MergeTaskResults(step.ExecutionContext.CommandResult, TaskResult.Canceled);
                    }
                }
                catch (Exception ex)
                {
                    // Log the error, set step result to falied if the current result is not canceled.
                    Trace.Error($"Caught exception from async command {command.Name}: {ex}");
                    step.ExecutionContext.Error(ex);

                    // if the step already canceled, don't set it to failed.
                    step.ExecutionContext.CommandResult = TaskResultUtil.MergeTaskResults(step.ExecutionContext.CommandResult, TaskResult.Failed);
                }
            }
            Trace.Info($"Step async command summary [Step:'{step.DisplayName}', TotalCommands:{step.ExecutionContext.AsyncCommands?.Count ?? 0}, CommandResult:{step.ExecutionContext.CommandResult}]");

            // Merge executioncontext result with command result
            if (step.ExecutionContext.CommandResult != null)
            {
                step.ExecutionContext.Result = TaskResultUtil.MergeTaskResults(step.ExecutionContext.Result, step.ExecutionContext.CommandResult.Value);
                Trace.Info($"Step result merged with command result - Step: {step.DisplayName}, CommandResult:{step.ExecutionContext.CommandResult} FinalResult: {step.ExecutionContext.Result}");
            }

           // Fixup the step result if ContinueOnError.
            if (step.ExecutionContext.Result == TaskResult.Failed && step.ContinueOnError)
            {
                step.ExecutionContext.Result = TaskResult.SucceededWithIssues;
                Trace.Info($"Step result updated due to ContinueOnError: '{step.DisplayName}', Result: Failed -> SucceededWithIssues");
            }
            else
            {
                Trace.Info($"Step result: '{step.DisplayName}', Result: {step.ExecutionContext.Result}");
            }

            // Complete the step context.
            step.ExecutionContext.Section(StringUtil.Loc("StepFinishing", step.DisplayName));
            step.ExecutionContext.Complete();
            Trace.Info($"Step execution summary - Step: '{step.DisplayName}', FinalResult: {step.ExecutionContext.Result}");
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
