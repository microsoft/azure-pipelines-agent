// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public sealed class FakeResourceMetricsManager : AgentService, IResourceMetricsManager
    {
        public async Task Run() { }
        public void Setup(IExecutionContext context) { }
        public void SetContext(IExecutionContext context) { }

        public void Dispose() { }
    }
}