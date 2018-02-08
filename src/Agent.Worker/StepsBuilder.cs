using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Expressions;
using Microsoft.TeamFoundation.DistributedTask.ServiceEndpoints;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using System.Linq;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(StepsBuilder))]
    public interface IStepsBuilder : IAgentService
    {
        List<IStep> Result { get; }
        void Build(IExecutionContext context, IList<Pipelines.JobStep> steps, Variables sharedVariables = null);
    }

    public class StepsBuilder : AgentService, IStepsBuilder
    {
        private readonly List<IStep> _result = new List<IStep>();

        Int32 _preInjectIndex = 0;
        Int32 _mainInjectIndex = 0;
        Int32 _postInjectIndex = 0;

        public List<IStep> Result => _result;

        public void AddPreStep(IStep step)
        {
            _result.Insert(_preInjectIndex, step);
            _preInjectIndex++;
            _mainInjectIndex++;
            _postInjectIndex++;
        }

        public void AddMainStep(IStep step)
        {
            _result.Insert(_mainInjectIndex, step);
            _mainInjectIndex++;
            _postInjectIndex++;
        }

        public void AddPostStep(IStep step)
        {
            _result.Insert(_postInjectIndex, step);
        }

        public void InsertStep(IStep step, int index)
        {
            if (index <= _preInjectIndex)
            {
                _result.Insert(index, step);
                _preInjectIndex++;
                _mainInjectIndex++;
                _postInjectIndex++;
            }
            else if (index <= _mainInjectIndex)
            {
                _result.Insert(index, step);
                _mainInjectIndex++;
                _postInjectIndex++;
            }
            else
            {
                _result.Insert(index, step);
            }
        }

        public void Build(IExecutionContext context, IList<Pipelines.JobStep> steps, Variables sharedVariables = null)
        {
            // Build up a basic list of steps for the job, expand warpper task
            var taskManager = HostContext.GetService<ITaskManager>();
            foreach (var step in steps)
            {
                if (step.Type == Pipelines.StepType.Task)
                {
                    var task = step as Pipelines.TaskStep;
                    var taskLoadResult = taskManager.Load(context, task);

                    // Add pre-job steps from Tasks
                    if (taskLoadResult.PreScopeStep != null)
                    {
                        Trace.Info($"Adding Pre-Job task step {step.DisplayName}.");
                        if (sharedVariables != null)
                        {
                            taskLoadResult.PreScopeStep.ScopeSharedVariables = sharedVariables;
                        }
                        AddPreStep(taskLoadResult.PreScopeStep);
                    }

                    // Add execution steps from Tasks
                    if (taskLoadResult.MainScopeStep != null)
                    {
                        Trace.Verbose($"Adding task step {step.DisplayName}.");
                        if (sharedVariables != null)
                        {
                            taskLoadResult.MainScopeStep.ScopeSharedVariables = sharedVariables;
                        }
                        AddMainStep(taskLoadResult.MainScopeStep);
                    }

                    // Add post-job steps from Tasks
                    if (taskLoadResult.PostScopeStep != null)
                    {
                        Trace.Verbose($"Adding Post-Job task step {step.DisplayName}.");
                        if (sharedVariables != null)
                        {
                            taskLoadResult.PostScopeStep.ScopeSharedVariables = sharedVariables;
                        }
                        AddPostStep(taskLoadResult.PostScopeStep);
                    }
                }
                else if (step.Type == Pipelines.StepType.Group)
                {
                    var group = step as Pipelines.GroupStep;
                    Trace.Verbose($"Adding group step {step.DisplayName}.");
                    var groupRunner = HostContext.CreateService<IGroupRunner>();
                    groupRunner.Group = group;
                    if (!string.IsNullOrEmpty(group.Condition))
                    {
                        context.Debug($"Group step '{group.DisplayName}' has following condition: '{group.Condition}'.");
                        var expression = HostContext.GetService<IExpressionManager>();
                        groupRunner.Condition = expression.Parse(context, group.Condition);
                    }
                    else
                    {
                        groupRunner.Condition = ExpressionManager.Succeeded;
                    }

                    AddMainStep(groupRunner);
                }
            }

            if (context.Containers.Count > 0)
            {
                // Add container related operation for steps in same level
                // Ex:
                //    - script: set             - start container: dev
                //      container: dev    ==>   - script in dev: set
                //    - script: set             - stop container: dev
                //      container: test         - start container: test 
                //                              - script in test: set
                //                              - stop container: test
                // ---------------------------------------------------------
                //    - script: set             - start container: dev
                //      container: dev    ==>   - script in dev: set
                //    - script: set             - script in dev: set
                //      container: dev          - stop container: test
                // ---------------------------------------------------------
                //    - script: set             - script in host: set
                //    - group: private    ==>   - group:
                //      steps:                    - start container: dev
                //        - script: set           - script in dev: set
                //        - script: set           - script in dev: set
                //      container: dev            - stop container: dev 
                //    - script: set             - script in host: set
                // ---------------------------------------------------------
                //    - script: set             - script in host: set
                //    - group: private    ==>   - start container: dev
                //      steps:                  - group:
                //        - script: set           - script in dev: set
                //        - script: set           - script in dev: set
                //      container: dev          - script in dev: set
                //    - script: set             - stop container: dev 
                //      container: dev
                var containerProvider = HostContext.GetService<IContainerOperationProvider>();
                // Inject container create/stop steps to the jobSteps list.
                foreach (var container in context.Containers)
                {
                    context.Debug($"Check where container '{container.ContainerName}' needs to be created with image '{container.ContainerImage}'");
                    if (container.ContainerCreateStepAssigned)
                    {
                        context.Debug($"Container '{container.ContainerName}' is already assigned start/stop step at parent scope level (group/job)");
                        continue;
                    }

                    var containerStepInjectPosition = ScanStepsContainerSetting(Result, container);
                    if (containerStepInjectPosition.ContainerFirstUsePosition == -1 &&
                        containerStepInjectPosition.ContainerLastUsePosition == -1)
                    {
                        context.Debug($"None of step that at current level is reference container '{container.ContainerName}'.");
                    }
                    else
                    {
                        if (containerStepInjectPosition.ContainerFirstUsePosition == containerStepInjectPosition.ContainerLastUsePosition &&
                            Result[containerStepInjectPosition.ContainerFirstUsePosition] is IGroupRunner)
                        {
                            context.Debug($"Group step '{Result[containerStepInjectPosition.ContainerFirstUsePosition].DisplayName}' will inject container create/stop step later.");
                        }
                        else
                        {
                            context.Debug($"Inject container stop for '{container.ContainerName}' after step '{Result[containerStepInjectPosition.ContainerLastUsePosition].DisplayName}'");
                            InsertStep(containerProvider.GetContainerStopStep(container), containerStepInjectPosition.ContainerLastUsePosition + 1);

                            context.Debug($"Inject container start for '{container.ContainerName}' before step '{Result[containerStepInjectPosition.ContainerFirstUsePosition].DisplayName}'");
                            InsertStep(containerProvider.GetContainerStartStep(container), containerStepInjectPosition.ContainerFirstUsePosition);

                            context.Debug($"Container '{container.ContainerName}' is assigned container start/stop step.");
                            container.ContainerCreateStepAssigned = true;
                        }
                    }
                }
            }
        }

        // Scan the step list find the first and last step that use the container
        private ContainerStepScanResult ScanStepsContainerSetting(List<IStep> steps, ContainerInfo container)
        {
            ArgUtil.NotNull(steps, nameof(steps));
            ArgUtil.NotNull(container, nameof(container));

            ContainerStepScanResult stepInjectPosition = new ContainerStepScanResult();
            foreach (var step in steps)
            {
                int currentIndex = steps.IndexOf(step);
                if (step is ITaskRunner)
                {
                    // the step is a task, check whether task use container
                    var task = step as ITaskRunner;
                    if (!string.IsNullOrEmpty(task.Task.Container) &&
                        task.Task.Container.Equals(container.ContainerName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (stepInjectPosition.ContainerFirstUsePosition == -1)
                        {
                            // Set first use position since the container haven't been used.
                            stepInjectPosition.ContainerFirstUsePosition = currentIndex;
                        }

                        // Always update last use position
                        stepInjectPosition.ContainerLastUsePosition = currentIndex;
                    }
                }
                else if (step is IGroupRunner)
                {
                    // the step is a group, check whether group use container or any step within the group use it
                    var group = step as IGroupRunner;
                    if (!string.IsNullOrEmpty(group.Group.Container) &&
                        group.Group.Container.Equals(container.ContainerName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (stepInjectPosition.ContainerFirstUsePosition == -1)
                        {
                            // Set first use position since the container haven't been used.
                            stepInjectPosition.ContainerFirstUsePosition = currentIndex;
                        }

                        // Always update last use position
                        stepInjectPosition.ContainerLastUsePosition = currentIndex;
                    }
                    else
                    {
                        // the entire group doesn't use container, but check whether task within group use container
                        foreach (var task in group.Group.Steps)
                        {
                            if (!string.IsNullOrEmpty(task.Container) &&
                                task.Container.Equals(container.ContainerName, StringComparison.OrdinalIgnoreCase))
                            {
                                if (stepInjectPosition.ContainerFirstUsePosition == -1)
                                {
                                    // Set first use position since the container haven't been used.
                                    stepInjectPosition.ContainerFirstUsePosition = currentIndex;
                                }

                                // Always update last use position
                                stepInjectPosition.ContainerLastUsePosition = currentIndex;
                            }
                        }
                    }
                }
                else
                {
                    // JobExtensionRunner will hit here which should not need container at all.
                }
            }

            return stepInjectPosition;
        }

        private class ContainerStepScanResult
        {
            public ContainerStepScanResult()
            {
                this.ContainerFirstUsePosition = -1;
                this.ContainerLastUsePosition = -1;
            }

            public int ContainerFirstUsePosition { get; set; }
            public int ContainerLastUsePosition { get; set; }
        }
    }
}