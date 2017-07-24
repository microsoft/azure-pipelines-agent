using Microsoft.VisualStudio.Services.Agent.Worker.Build;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(StepsQueue))]

    public interface IStepsQueue
    {
        IEnumerable<IStep> GetPreJobSteps();
        IEnumerable<IStep> GetJobSteps();
        IEnumerable<IStep> GetPostJobSteps();
    }

    public class StepsQueue : IStepsQueue
    {
        private readonly JobInitializeResult initializeResult;
        private readonly IExecutionContext executionContext;
        private readonly Tracing trace;
        private readonly bool developerMode;
        private readonly IBuildDirectoryManager directoryManager;
        private int step = 0;
        
        public StepsQueue(IHostContext context, IExecutionContext executionContext, JobInitializeResult initializeResult) {
            this.developerMode = true;
            this.trace = context.GetTrace(nameof(Program));
            this.initializeResult = initializeResult;
            this.executionContext = executionContext;
            this.directoryManager = context.GetService<IBuildDirectoryManager>();

            // trace out all steps
            trace.Info($"Total pre-job steps: {initializeResult.PreJobSteps.Count}.");
            trace.Verbose($"Pre-job steps: '{string.Join(", ", initializeResult.PreJobSteps.Select(x => x.DisplayName))}'");

            trace.Info($"Total job steps: {initializeResult.JobSteps.Count}.");
            trace.Verbose($"Job steps: '{string.Join(", ", initializeResult.JobSteps.Select(x => x.DisplayName))}'");

            trace.Info($"Total post-job steps: {initializeResult.PostJobStep.Count}.");
            trace.Verbose($"Post-job steps: '{string.Join(", ", initializeResult.PostJobStep.Select(x => x.DisplayName))}'");
        }

        public void NextStep()
        {
        }

        public void RepeatStep(IStep next)
        {
            // Restore state
        }
        
        public IEnumerable<IStep> GetJobSteps()
        {
            if (developerMode)
            {
                return GetJobStepsEnumerator();
            }
            return initializeResult.JobSteps;
        }

        private IEnumerable<IStep> GetJobStepsEnumerator()
        {
            // TODO: wait for UI
            foreach (IStep s in initializeResult.JobSteps)
            {
                if (s == null)
                {
                    trace.Verbose($"Completed Jobs queue.  Total steps: {step}");
                    break;
                }

                trace.Verbose($"Current job{s.DisplayName}");
                yield return s;
                trace.Verbose($"Completed job{s.DisplayName}: saving state");
                step++;
                directoryManager.SaveDevelopmentSnapshot(executionContext, "step_" + step);
            }
        }

        public IEnumerable<IStep> GetPostJobSteps()
        {
            return initializeResult.PostJobStep;
        }

        public IEnumerable<IStep> GetPreJobSteps()
        {
            return initializeResult.PreJobSteps;
        }
    }
}
