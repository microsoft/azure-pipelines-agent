// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Worker;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public sealed class FakeResourceMetricsManager : AgentService, IResourceMetricsManager
    {
        public Task RunDebugResourceMonitorAsync() { return Task.CompletedTask; }
        public Task RunMemoryUtilizationMonitorAsync() { return Task.CompletedTask; }
        public Task RunDiskSpaceUtilizationMonitorAsync() { return Task.CompletedTask; }
        public Task RunCpuUtilizationMonitorAsync(string taskId) { return Task.CompletedTask; }
        public void SetContext(IExecutionContext context) { }

        public void Dispose() { }
    }
}