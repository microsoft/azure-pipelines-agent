// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Worker;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public sealed class FakeResourceMetricsManager : AgentService, IResourceMetricsManager
    {
        public async Task RunDebugResourceMonitor() { }
        public async Task RunMemoryUtilizationMonitor() { }
        public async Task RunDiskSpaceUtilizationMonitor() { }
        public async Task RunCpuUtilizationMonitor(string taskId) { }
        public void Setup(IExecutionContext context) { }
        public void SetContext(IExecutionContext context) { }

        public void Dispose() { }
    }
}