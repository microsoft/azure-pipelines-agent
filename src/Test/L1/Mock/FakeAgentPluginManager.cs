// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.VisualStudio.Services.Agent.Worker;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public class FakeAgentPluginManager : AgentPluginManager
    {
        public override void Initialize(IHostContext hostContext)
        {
            // Inject any plugin mocks here.
            // Each injection should be paired with a removal of the plugin being mocked.
            ReplacePlugin("Agent.Plugins.Repository.CheckoutTask, Agent.Plugins", "Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker.FakeCheckoutTask, Test");
            base.Initialize(hostContext);
        }

        private void ReplacePlugin(string existingPlugin, string fakePlugin)
        {
            if (!_taskPlugins.Contains(existingPlugin))
            {
                throw new Exception($"{existingPlugin} must exist in _taskPlugins in order to be replaced");
            }
            if (_taskPlugins.Contains(fakePlugin))
            {
                throw new Exception($"{fakePlugin} already exists in _taskPlugins");
            }
            _taskPlugins.Remove(existingPlugin);
            _taskPlugins.Add(fakePlugin);
        }
    }
}
