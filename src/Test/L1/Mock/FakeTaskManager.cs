using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.Linq;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public class FakeTaskManager : TaskManager
    {
        public override Definition Load(Pipelines.TaskStep task)
        {
            Definition d = base.Load(task);
            if (task.Reference.Id == Pipelines.PipelineConstants.CheckoutTask.Id && task.Reference.Version == Pipelines.PipelineConstants.CheckoutTask.Version)
            {
                AgentPluginHandlerData checkoutHandlerData = new AgentPluginHandlerData();
                checkoutHandlerData.Target = "Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker.FakeCheckoutTask, Test";
                d.Data.Execution = new ExecutionData()
                {
                    AgentPlugin = checkoutHandlerData
                };
            }

            return d;
        }
    }
}