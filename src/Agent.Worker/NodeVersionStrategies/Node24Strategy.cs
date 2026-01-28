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
    public sealed class Node24Strategy : INodeVersionStrategy
    {
        private const string Node24Folder = "node24";

        public NodeRunnerInfo CanHandle(TaskContext context, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo)
        {
            bool useNode24Globally = AgentKnobs.UseNode24.GetValue(executionContext).AsBoolean();
            bool hasNode24Handler = context.HandlerData is Node24HandlerData;
            bool useNode24WithHandlerData = AgentKnobs.UseNode24withHandlerData.GetValue(executionContext).AsBoolean();
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean();

            // Check if Node24 binary exists on this platform (e.g., not available on win-x86)
            if (!IsNodeFolderExist(Node24Folder, executionContext))
            {
                executionContext.Debug("[Node24Strategy] Node24 binary not available on this platform, returning null for fallback");
                PublishNodeFallbackTelemetry(executionContext, "Node24", "NodeNotAvailable", context);
                return null;
            }

            if (useNode24Globally)
            {
                executionContext.Debug("[Node24Strategy] AGENT_USE_NODE24=true → Global override");
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected via global AGENT_USE_NODE24 override", executionContext, glibcInfo);
            }
            
            if (hasNode24Handler)
            {
                if (useNode24WithHandlerData)
                {
                    return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected for Node24 task with handler knob enabled", executionContext, glibcInfo);
                }
                else
                {
                    return new NodeRunnerInfo
                    {
                        NodePath = null,
                        NodeVersion = NodeVersion.Node20,
                        Reason = "Node24 task detected but handler knob disabled, falling back to Node20",
                        Warning = null
                    };
                }
            }
            
            if (eolPolicyEnabled)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Upgraded from end-of-life Node version due to EOL policy", executionContext, glibcInfo);
            }
            
            return null;
        }

        private NodeRunnerInfo DetermineNodeVersionSelection(TaskContext context, bool eolPolicyEnabled, string baseReason, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo)
        {
            string systemType = context.Container != null ? "container" : "agent";
            
            if (!glibcInfo.Node24HasGlibcError)
            {
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node24,
                    Reason = baseReason,
                    Warning = null
                };
            }

            if (!glibcInfo.Node20HasGlibcError)
            {
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node20,
                    Reason = $"{baseReason}, fallback to Node20 due to Node24 glibc compatibility issue",
                    Warning = StringUtil.Loc("NodeGlibcFallbackWarning", systemType, "Node24", "Node20")
                };
            }

            if (eolPolicyEnabled)
            {
                executionContext.Debug("[Node24Strategy] Would need Node16 but EOL policy being enabled this is not supported");
                string handlerType = context.HandlerData != null ? context.HandlerData.GetType().Name : "UnknownHandlerData";
                throw new NotSupportedException($"No compatible Node.js version available for host execution. Handler type: {handlerType}. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_RESTRICT_EOL_NODE_VERSIONS=false");
            }

            return new NodeRunnerInfo
            {
                NodePath = null,
                NodeVersion = NodeVersion.Node16,
                Reason = $"{baseReason}, fallback to Node16 due to both Node24 and Node20 glibc compatibility issues",
                Warning = StringUtil.Loc("NodeGlibcFallbackWarning", systemType, "Node24 or Node20", "Node16")
            };
        }

        public NodeRunnerInfo CanHandleInContainer(TaskContext context, IExecutionContext executionContext, IDockerCommandManager dockerManager)
        {
            if (context.Container == null)
            {
                executionContext.Debug("[Node24Strategy] CanHandleInContainer called but no container context provided");
                return null;
            }

            bool useNode24ToStartContainer = AgentKnobs.UseNode24ToStartContainer.GetValue(executionContext).AsBoolean();
            
            if (!useNode24ToStartContainer)
            {
                executionContext.Debug("[Node24Strategy] UseNode24ToStartContainer=false, cannot handle container");
                return null;
            }

            executionContext.Debug("[Node24Strategy] UseNode24ToStartContainer=true, checking Node24 availability in container");

            try
            {
                if (NodeContainerTestHelper.CanExecuteNodeInContainer(context, executionContext, dockerManager, NodeVersion.Node24, "Node24Strategy"))
                {
                    return new NodeRunnerInfo
                    {
                        NodePath = null,
                        NodeVersion = NodeVersion.Node24,
                        Reason = "Node24 available in container via UseNode24ToStartContainer knob",
                        Warning = null
                    };
                }
                else
                {
                    executionContext.Debug("[Node24Strategy] Node24 test failed in container, returning null for fallback");
                    return null;
                }
            }
            catch (Exception ex)
            {
                executionContext.Warning($"[Node24Strategy] Failed to test Node24 in container: {ex.Message}");
                return null;
            }
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
                    { "Strategy", "Node24Strategy" },
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
                executionContext.Debug($"[Node24Strategy] Published fallback telemetry: {requestedNodeVersion} -> {fallbackNodeVersion ?? "NextStrategy"}, Reason: {fallbackReason}, Architecture: {architecture}");
            }
            catch (Exception ex)
            {
                executionContext.Debug($"[Node24Strategy] Failed to publish fallback telemetry: {ex.Message}");
            }
        }
    }
}