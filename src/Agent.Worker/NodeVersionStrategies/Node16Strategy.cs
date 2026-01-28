// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Agent.Sdk;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    public sealed class Node16Strategy : INodeVersionStrategy
    {
        private const string Node16Folder = "node16";

        public NodeRunnerInfo CanHandle(TaskContext context, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo)
        {
            bool hasNode16Handler = context.HandlerData is Node16HandlerData;
            bool hasNode20Handler = context.HandlerData is Node20_1HandlerData;
            bool hasNode24Handler = context.HandlerData is Node24HandlerData;
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean();

            // Check if Node16 binary exists on this platform
            if (!IsNodeFolderExist(Node16Folder, executionContext))
            {
                executionContext.Debug("[Node16Strategy] Node16 binary not available on this platform, returning null");
                PublishNodeFallbackTelemetry(executionContext, "Node16", "NodeNotAvailable", context);
                return null;
            }

            if (hasNode16Handler)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected for Node16 task handler");
            }

            // Handle Node24 tasks as fallback when Node24Strategy and Node20Strategy returned null
            if (hasNode24Handler)
            {
                executionContext.Debug("[Node16Strategy] Handling Node24 task as fallback (Node24 and Node20 not available on this platform)");
                PublishNodeFallbackTelemetry(executionContext, "Node24", "NodeNotAvailable", context, "Node16");
                return DetermineNodeVersionSelectionWithFallbackWarning(context, eolPolicyEnabled, 
                    "Fallback from Node24 (Node24 and Node20 not available on this platform)", "Node24", executionContext);
            }

            // Handle Node20 tasks as fallback when Node20Strategy returned null
            if (hasNode20Handler)
            {
                executionContext.Debug("[Node16Strategy] Handling Node20 task as fallback (Node20 not available on this platform)");
                PublishNodeFallbackTelemetry(executionContext, "Node20", "NodeNotAvailable", context, "Node16");
                return DetermineNodeVersionSelectionWithFallbackWarning(context, eolPolicyEnabled, 
                    "Fallback from Node20 (not available on this platform)", "Node20", executionContext);
            }

            return null;
        }

        private NodeRunnerInfo DetermineNodeVersionSelection(TaskContext context, bool eolPolicyEnabled, string baseReason)
        {
            if (eolPolicyEnabled)
            {
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node16"));
            }

            return new NodeRunnerInfo
            {
                NodePath = null,
                NodeVersion = NodeVersion.Node16,
                Reason = baseReason,
                Warning = StringUtil.Loc("NodeEOLWarning", "Node16")
            };
        }

        public NodeRunnerInfo CanHandleInContainer(TaskContext context, IExecutionContext executionContext, IDockerCommandManager dockerManager)
        {
            if (context.Container == null)
            {
                executionContext.Debug("[Node16Strategy] CanHandleInContainer called but no container context provided");
                return null;
            }

            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean();

            if (eolPolicyEnabled)
            {
                executionContext.Debug("[Node16Strategy] Node16 blocked by EOL policy in container");
                throw new NotSupportedException("No compatible Node.js version available for container execution. Node16 is blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks.");
            }

            executionContext.Debug("[Node16Strategy] Providing Node16 as final fallback for container");

            return new NodeRunnerInfo
            {
                NodePath = null,
                NodeVersion = NodeVersion.Node16,
                Reason = "Final fallback to Node16 for container execution",
                Warning = "Using Node16 in container. Consider updating to Node20 or Node24 for better performance and security."
            };
        }

        private NodeRunnerInfo DetermineNodeVersionSelectionWithFallbackWarning(TaskContext context, bool eolPolicyEnabled, string baseReason, string originalNodeVersion, IExecutionContext executionContext)
        {
            if (eolPolicyEnabled)
            {
                executionContext.Debug($"[Node16Strategy] Cannot fallback to Node16 - blocked by EOL policy");
                throw new NotSupportedException($"No compatible Node.js version available. {originalNodeVersion} is not available on this platform and Node16 is blocked by EOL policy. " +
                    "Please update your pipeline to use supported Node versions or disable EOL policy: Set AGENT_RESTRICT_EOL_NODE_VERSIONS=false");
            }

            string systemType = context.Container != null ? "container" : "agent";
            return new NodeRunnerInfo
            {
                NodePath = null,
                NodeVersion = NodeVersion.Node16,
                Reason = baseReason,
                Warning = $"{originalNodeVersion} is not available on this platform (architecture: {RuntimeInformation.ProcessArchitecture}). Using Node16 instead. " +
                    $"Please upgrade the operating system of the {systemType} to remain compatible with future updates of tasks: " +
                    "https://github.com/nodesource/distributions"
            };
        }

        private bool IsNodeFolderExist(string nodeFolderName, IExecutionContext executionContext)
        {
            var hostContext = executionContext.GetHostContext();
            var nodePath = Path.Combine(
                hostContext.GetDirectory(WellKnownDirectory.Externals),
                nodeFolderName,
                "bin",
                $"node{IOUtil.ExeExtension}");
            return File.Exists(nodePath);
        }

        private void PublishNodeFallbackTelemetry(IExecutionContext executionContext, string requestedNodeVersion, string fallbackReason, TaskContext context, string fallbackNodeVersion = null)
        {
            try
            {
                var systemVersion = PlatformUtil.GetSystemVersion();
                string architecture = RuntimeInformation.ProcessArchitecture.ToString();
                bool inContainer = context.Container != null;

                Dictionary<string, string> telemetryData = new Dictionary<string, string>
                {
                    { "RequestedNodeVersion", requestedNodeVersion },
                    { "FallbackNodeVersion", fallbackNodeVersion ?? "NextStrategy" },
                    { "FallbackReason", fallbackReason },
                    { "Strategy", "Node16Strategy" },
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
                executionContext.Debug($"[Node16Strategy] Published fallback telemetry: {requestedNodeVersion} -> {fallbackNodeVersion ?? "NextStrategy"}, Reason: {fallbackReason}, Architecture: {architecture}");
            }
            catch (Exception ex)
            {
                executionContext.Debug($"[Node16Strategy] Failed to publish fallback telemetry: {ex.Message}");
            }
        }

    }
}