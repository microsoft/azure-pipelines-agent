// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Worker;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public sealed class FakeResourceMetricsManager : AgentService, IResourceMetricsManager
    {
        public Task RunDebugResourceMonitor() { return Task.CompletedTask; }
        public Task RunMemoryUtilizationMonitor() { return Task.CompletedTask; }
        public Task RunDiskSpaceUtilizationMonitor() { return Task.CompletedTask; }
        public Task RunCpuUtilizationMonitor(string taskId) { return Task.CompletedTask; }
        public void Setup(IExecutionContext context) { }
        public void SetContext(IExecutionContext context) { }

        public void Dispose() { }
    }
}