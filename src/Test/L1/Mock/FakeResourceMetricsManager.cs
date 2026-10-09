// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Worker;
using System;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public sealed class FakeResourceMetricsManager : AgentService, IResourceMetricsManager
    {
        public void StartMonitoring(IExecutionContext jobContext, bool enableDebugOutput) { }
        public IDisposable AttachTask(IExecutionContext taskContext, Guid taskId, bool enableWarnings) { return null; }
        public Task StopMonitoringAsync() { return Task.CompletedTask; }

        public void Dispose() { }
    }
}