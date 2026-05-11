// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Newtonsoft.Json;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Telemetry
{
    [ServiceLocator(Default = typeof(WorkerCrashTelemetryPublisher))]
    public interface IWorkerCrashTelemetryPublisher : IAgentService
    {
        Task PublishWorkerCrashTelemetryAsync(IHostContext hostContext, Guid jobId, int exitCode, string tracePoint);
    }

    public sealed class WorkerCrashTelemetryPublisher : AgentService, IWorkerCrashTelemetryPublisher
    {
        public async Task PublishWorkerCrashTelemetryAsync(IHostContext hostContext, Guid jobId, int exitCode, string tracePoint)
        {
            try
            {
                var telemetryPublisher = hostContext.GetService<IAgenetListenerTelemetryPublisher>();
                
                var telemetryData = new Dictionary<string, object>
                {
                    ["JobId"] = jobId.ToString(),
                    ["ExitCode"] = exitCode.ToString(),
                    ["TracePoint"] = tracePoint
                };

                var command = new Command("telemetry", "publish")
                {
                    Data = JsonConvert.SerializeObject(telemetryData)
                };
                command.Properties.Add("area", "AzurePipelinesAgent");
                command.Properties.Add("feature", "WorkerCrash");

                await telemetryPublisher.PublishEvent(hostContext, command);
                Trace.Info($"Published worker crash telemetry for job {jobId} with exit code {exitCode}");
            }
            catch (Exception ex)
            {
                Trace.Warning($"Failed to publish worker crash telemetry: {ex}");
            }
        }
    }
}