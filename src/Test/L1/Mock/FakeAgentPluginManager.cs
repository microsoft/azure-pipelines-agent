using System;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Worker;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public class FakeAgentPluginManager : AgentPluginManager
    {
        public override void Initialize(IHostContext hostContext)
        {
            _taskPlugins.Remove("Agent.Plugins.Repository.CheckoutTask, Agent.Plugins");
            _taskPlugins.Add("Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker.FakeCheckoutTask, Test");
            base.Initialize(hostContext);
        }
    }
}