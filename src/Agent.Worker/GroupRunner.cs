using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Expressions;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using System.Threading;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(GroupRunner))]
    public interface IGroupRunner : IStep, IAgentService
    {
        Pipelines.GroupStep Group { get; set; }
    }

    public sealed class GroupRunner : AgentService, IGroupRunner
    {
        private List<IStep> _steps;

        public Pipelines.GroupStep Group { get; set; }

        public ContainerInfo Container => null;

        public INode Condition { get; set; }

        public bool ContinueOnError => Group?.ContinueOnError ?? default(bool);

        public string DisplayName => Group?.DisplayName;

        public bool Enabled => Group?.Enabled ?? default(bool);

        public IExecutionContext ExecutionContext { get; set; }

        public TimeSpan? Timeout => (Group?.TimeoutInMinutes ?? 0) > 0 ? (TimeSpan?)TimeSpan.FromMinutes(Group.TimeoutInMinutes) : null;

        public List<Pipelines.ContainerReference> Containers { get; set; }

        public void InitializeStep(IExecutionContext jobExecutionContext)
        {
            ExecutionContext = jobExecutionContext.CreateChild(Group.Id, Group.DisplayName, Group.Name);

            // populate container info to each step in the group.
            if (!string.IsNullOrEmpty(Group.Container))
            {
                foreach (var step in Group.Steps)
                {
                    step.Container = Group.Container;
                    jobExecutionContext.Debug($"{step.DisplayName}: {step.Container}");
                }
            }
        }

        public async Task RunAsync()
        {
            // Validate args.
            Trace.Entering();
            ArgUtil.NotNull(ExecutionContext, nameof(ExecutionContext));
            ArgUtil.NotNull(Group, nameof(Group));

            // clone the variable dictionary variable within the group will not flow out by default
            Variables groupSharedVariables = ExecutionContext.Variables.Clone();

            var groupStepsBuilder = HostContext.CreateService<IStepsBuilder>();
            groupStepsBuilder.Build(ExecutionContext, Group.Steps.Select(x => x as Pipelines.JobStep).ToList(), groupSharedVariables);
            _steps = groupStepsBuilder.Result;

            // create task execution context for all steps in the group
            foreach (var step in _steps)
            {
                ArgUtil.NotNull(step, step.DisplayName);
                step.InitializeStep(ExecutionContext);
            }

            var stepsRunner = HostContext.CreateService<IStepsRunner>();
            try
            {
                stepsRunner.OnCancellation += delegate (object sender, EventArgs args)
                {
                    groupSharedVariables.Agent_JobStatus = TaskResult.Canceled;
                };

                await stepsRunner.RunAsync(ExecutionContext, _steps);
            }
            catch (Exception ex)
            {
                // StepRunner should never throw exception out.
                // End up here mean there is a bug in StepRunner
                // Log the error and fail the job.
                Trace.Error($"Caught exception from job steps {nameof(StepsRunner)}: {ex}");
                ExecutionContext.Error(ex);
                ExecutionContext.Result = TaskResult.Failed;
            }

            // map group output variables
            if (Group.Outputs.Count > 0)
            {
                foreach (var output in Group.Outputs)
                {
                    ExecutionContext.Debug($"Mapping task output '{output.Value}' to group output '{output.Key}'.");
                    Variable taskOutput = groupSharedVariables.GetRaw(output.Value);

                    if (taskOutput != null)
                    {
                        ExecutionContext.SetVariable(output.Key, taskOutput.Value, taskOutput.Secret, true);
                    }
                    else
                    {
                        ExecutionContext.Debug($"Task output '{output.Value}' is not set.");
                    }
                }
            }

            // StepRunner expect each step to throw on cancellation
            ExecutionContext.CancellationToken.ThrowIfCancellationRequested();
        }
    }
}
