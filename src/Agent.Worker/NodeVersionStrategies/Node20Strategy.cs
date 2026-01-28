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
    public sealed class Node20Strategy : INodeVersionStrategy
    {
        private const string Node20Folder = "node20_1";

        public NodeRunnerInfo CanHandle(TaskContext context, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo)
        {
            bool useNode20Globally = AgentKnobs.UseNode20_1.GetValue(executionContext).AsBoolean();
            bool hasNode20Handler = context.HandlerData is Node20_1HandlerData;
            bool hasNode24Handler = context.HandlerData is Node24HandlerData;
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean();

            // Check if Node20 binary exists on this platform
            if (!IsNodeFolderExist(Node20Folder, executionContext))
            {
                executionContext.Debug("[Node20Strategy] Node20 binary not available on this platform, returning null for fallback");
                PublishNodeFallbackTelemetry(executionContext, "Node20", "NodeNotAvailable", context);
                return null;
            }
            
            if (useNode20Globally)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected via global AGENT_USE_NODE20_1 override", glibcInfo);
            }
            
            if (hasNode20Handler)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected for Node20 task handler", glibcInfo);
            }

            // Handle Node24 tasks as fallback when Node24Strategy returned null (Node24 not available)
            if (hasNode24Handler)
            {
                executionContext.Debug("[Node20Strategy] Handling Node24 task as fallback (Node24 not available on this platform)");
                PublishNodeFallbackTelemetry(executionContext, "Node24", "NodeNotAvailable", context, "Node20");
                return DetermineNodeVersionSelectionWithFallbackWarning(context, eolPolicyEnabled, 
                    "Fallback from Node24 (not available on this platform)", glibcInfo);
            }
            
            if (eolPolicyEnabled)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Upgraded from end-of-life Node version due to EOL policy", glibcInfo);
            }
            
            return null;
        }

        private NodeRunnerInfo DetermineNodeVersionSelection(TaskContext context, bool eolPolicyEnabled, string baseReason, GlibcCompatibilityInfo glibcInfo)
        {
            if (!glibcInfo.Node20HasGlibcError)
            {
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node20,
                    Reason = baseReason,
                    Warning = null
                };
            }

            if (eolPolicyEnabled)
            {
                throw new NotSupportedException(StringUtil.Loc("NodeEOLFallbackBlocked", "Node20", "Node16"));
            }
            
            string systemType = context.Container != null ? "container" : "agent";
            return new NodeRunnerInfo
            {
                NodePath = null,
                NodeVersion = NodeVersion.Node16,
                Reason = $"{baseReason}, fallback to Node16 due to Node20 glibc compatibility issue",
                Warning = StringUtil.Loc("NodeGlibcFallbackWarning", systemType, "Node20", "Node16")
            };
        }

        public NodeRunnerInfo CanHandleInContainer(TaskContext context, IExecutionContext executionContext, IDockerCommandManager dockerManager)
        {
            if (context.Container == null)
            {
                executionContext.Debug("[Node20Strategy] CanHandleInContainer called but no container context provided");
                return null;
            }

            bool useNode20ToStartContainer = AgentKnobs.UseNode20ToStartContainer.GetValue(executionContext).AsBoolean();        
            if (!useNode20ToStartContainer)
            {
                executionContext.Debug("[Node20Strategy] UseNode20ToStartContainer=false, cannot handle container");
                return null;
            }

            executionContext.Debug("[Node20Strategy] UseNode20ToStartContainer=true, checking Node20 availability in container");

            try
            {
                if (NodeContainerTestHelper.CanExecuteNodeInContainer(context, executionContext, dockerManager, NodeVersion.Node20, "Node20Strategy"))
                {
                    return new NodeRunnerInfo
                    {
                        NodePath = null,
                        NodeVersion = NodeVersion.Node20,
                        Reason = "Node20 available in container via UseNode20ToStartContainer knob",
                        Warning = null
                    };
                }
                else
                {
                    executionContext.Debug("[Node20Strategy] Node20 test failed in container, returning null for fallback");
                    return null;
                }
            }
            catch (Exception ex)
            {
                executionContext.Warning($"[Node20Strategy] Failed to test Node20 in container: {ex.Message}");
                return null;
            }
        }

        private NodeRunnerInfo DetermineNodeVersionSelectionWithFallbackWarning(TaskContext context, bool eolPolicyEnabled, string baseReason, GlibcCompatibilityInfo glibcInfo)
        {
            var result = DetermineNodeVersionSelection(context, eolPolicyEnabled, baseReason, glibcInfo);
            
            if (result != null)
            {
                string systemType = context.Container != null ? "container" : "agent";
                result.Warning = $"Node24 is not available on this platform (architecture: {RuntimeInformation.ProcessArchitecture}). Using {result.NodeVersion} instead. " +
                    $"Please upgrade the operating system of the {systemType} to remain compatible with future updates of tasks: " +
                    "https://github.com/nodesource/distributions";
            }
            
            return result;
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
                    { "Strategy", "Node20Strategy" },
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
                executionContext.Debug($"[Node20Strategy] Published fallback telemetry: {requestedNodeVersion} -> {fallbackNodeVersion ?? "NextStrategy"}, Reason: {fallbackReason}, Architecture: {architecture}");
            }
            catch (Exception ex)
            {
                executionContext.Debug($"[Node20Strategy] Failed to publish fallback telemetry: {ex.Message}");
            }
        }
    }
}