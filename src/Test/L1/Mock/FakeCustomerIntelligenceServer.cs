// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Worker.Telemetry;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebPlatform;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public class FakeCustomerIntelligenceServer : AgentService, ICustomerIntelligenceServer
    {
        public IList<CustomerIntelligenceEvent> events = new List<CustomerIntelligenceEvent>();

        public void Initialize(VssConnection connection)
        {
        }

        public Task PublishEventsAsync(CustomerIntelligenceEvent[] ciEvents)
        {
            events.AddRange(ciEvents);
            return Task.CompletedTask;
        }
    }
}