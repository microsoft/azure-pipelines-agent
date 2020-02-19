using System;
using Agent.Plugins.Repository;
using Agent.Sdk;
using System.Threading.Tasks;
using System.Threading;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public class FakeCheckoutTask : CheckoutTask
    {
        public override async Task RunAsync(AgentTaskPluginExecutionContext executionContext, CancellationToken token)
        {
            await Task.Delay(1);
        }
    }
}