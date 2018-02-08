using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Expressions;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    public sealed class JobExtensionRunner : IStep
    {
        private readonly Func<IExecutionContext, object, Task> _runAsync;

        private readonly object _data;

        public JobExtensionRunner(
            object data,
            Func<IExecutionContext, object, Task> runAsync,
            INode condition,
            string displayName)
        {
            _data = data;
            _runAsync = runAsync;
            Condition = condition;
            DisplayName = displayName;
        }

        public INode Condition { get; set; }
        public bool ContinueOnError => false;
        public string DisplayName { get; private set; }
        public bool Enabled => true;
        public IExecutionContext ExecutionContext { get; set; }
        public TimeSpan? Timeout => null;
        public ContainerInfo Container => null;

        public void InitializeStep(IExecutionContext jobExecutionContext)
        {
            ExecutionContext = jobExecutionContext.CreateChild(Guid.NewGuid(), DisplayName, nameof(JobExtensionRunner));
        }

        public async Task RunAsync()
        {
            await _runAsync(ExecutionContext, _data);
        }
    }
}
