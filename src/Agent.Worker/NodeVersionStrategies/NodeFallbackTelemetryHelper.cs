// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    /// <summary>
    /// Shared helper for publishing node fallback telemetry across all node version strategies.
    /// </summary>
    public static class NodeFallbackTelemetryHelper
    {
        public static void PublishTelemetry(
            IExecutionContext executionContext,
            string requestedNodeVersion,
            string fallbackReason,
            TaskContext context,
            string strategyName,
            string fallbackNodeVersion = null)
        {
            try
            {
                var systemVersion = PlatformUtil.GetSystemVersion();
                string architecture = RuntimeInformation.ProcessArchitecture.ToString();
                bool inContainer = context.Container != null;

                // Get task information from execution context variables
                string taskDisplayName = executionContext.Variables.Get(Constants.Variables.Task.DisplayName) ?? "";
                string taskInstanceName = executionContext.Variables.Get("system.taskInstanceName") ?? "";

                Dictionary<string, string> telemetryData = new Dictionary<string, string>
                {
                    { "RequestedNodeVersion", requestedNodeVersion },
                    { "FallbackNodeVersion", fallbackNodeVersion ?? "NextStrategy" },
                    { "FallbackReason", fallbackReason },
                    { "Strategy", strategyName },
                    { "TaskDisplayName", taskDisplayName },
                    { "TaskInstanceName", taskInstanceName },
                    { "OS", PlatformUtil.HostOS.ToString() },
                    { "OSName", systemVersion?.Name?.ToString() ?? "" },
                    { "OSVersion", systemVersion?.Version?.ToString() ?? "" },
                    { "Architecture", architecture },
                    { "IsContainer", inContainer.ToString() },
                    { "HandlerType", context.HandlerData?.GetType().Name ?? "Unknown" },
                    { "JobId", executionContext.Variables.System_JobId.ToString() },
                    { "PlanId", executionContext.Variables.Get(Constants.Variables.System.PlanId) ?? "" },
                    { "AgentName", executionContext.Variables.Get(Constants.Variables.Agent.Name) ?? "" },
                    { "AgentVersion", executionContext.Variables.Get(Constants.Variables.Agent.Version) ?? "" },
                    { "IsSelfHosted", executionContext.Variables.Get(Constants.Variables.Agent.IsSelfHosted) ?? "" }
                };

                executionContext.PublishTaskRunnerTelemetry(telemetryData);
                executionContext.Debug($"[{strategyName}] Published fallback telemetry: {requestedNodeVersion} -> {fallbackNodeVersion ?? "NextStrategy"}, Reason: {fallbackReason}, Architecture: {architecture}");
            }
            catch (Exception ex)
            {
                executionContext.Debug($"[{strategyName}] Failed to publish fallback telemetry: {ex.Message}");
            }
        }
    }
}
