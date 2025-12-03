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
        Task PublishWorkerCrashTelemetryAsync(IHostContext hostContext, Guid jobId, Guid? taskInstanceId, int exitCode);
        Task PublishEnhancedWorkerCrashTelemetryAsync(IHostContext hostContext, Guid jobId, Guid? taskInstanceId, int exitCode, TaskOrchestrationPlanReference plan, string crashType, string completionMethod);
    }

    public sealed class WorkerCrashTelemetryPublisher : AgentService, IWorkerCrashTelemetryPublisher
    {
        public async Task PublishWorkerCrashTelemetryAsync(IHostContext hostContext, Guid jobId, Guid? taskInstanceId, int exitCode)
        {
            try
            {
                var telemetryPublisher = hostContext.GetService<IAgenetListenerTelemetryPublisher>();
                
                var telemetryData = new Dictionary<string, object>
                {
                    ["JobId"] = jobId.ToString(),
                    ["TaskInstanceId"] = taskInstanceId?.ToString() ?? "N/A",
                    ["ExitCode"] = exitCode.ToString()
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
                Trace.Warning($"Failed to publish worker crash telemetry: {ex.Message}");
            }
        }

        public async Task PublishEnhancedWorkerCrashTelemetryAsync(IHostContext hostContext, Guid jobId, Guid? taskInstanceId, int exitCode, TaskOrchestrationPlanReference plan, string crashType, string completionMethod)
        {
            try
            {
                var telemetryPublisher = hostContext.GetService<IAgenetListenerTelemetryPublisher>();
                
                var telemetryData = new Dictionary<string, object>
                {
                    ["JobId"] = jobId.ToString(),
                    ["TaskInstanceId"] = taskInstanceId?.ToString() ?? "N/A", 
                    ["ExitCode"] = exitCode.ToString(),
                    ["PlanVersion"] = plan?.Version.ToString() ?? "Unknown",
                    ["CrashType"] = crashType,
                    ["CompletionMethod"] = completionMethod
                };

                var command = new Command("telemetry", "publish")
                {
                    Data = JsonConvert.SerializeObject(telemetryData)
                };
                command.Properties.Add("area", "AzurePipelinesAgent");
                command.Properties.Add("feature", "EnhancedWorkerCrashHandling");

                await telemetryPublisher.PublishEvent(hostContext, command);
                Trace.Info($"Published enhanced worker crash telemetry for job {jobId} with exit code {exitCode}, plan version {plan?.Version}, completion method {completionMethod}");
            }
            catch (Exception ex)
            {
                Trace.Warning($"Failed to publish enhanced worker crash telemetry: {ex.Message}");
            }
        }
    }
}