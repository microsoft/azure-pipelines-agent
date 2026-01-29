// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Sdk;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;

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

            var hostContext = executionContext.GetHostContext();
            var nodeHandlerHelper = hostContext.GetService<INodeHandlerHelper>();

            // Check if Node24 binary exists on this platform (e.g., not available on win-x86)
            if (!nodeHandlerHelper.IsNodeFolderExist(Node24Folder, hostContext))
            {
                executionContext.Debug("[Node24Strategy] Node24 binary not available on this platform, checking fallback options");
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Node24 not available on this platform", executionContext, glibcInfo, skipNode24Check: true);
            }

            if (useNode24Globally)
            {
                executionContext.Debug("[Node24Strategy] AGENT_USE_NODE24=true → Global override");
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected via global AGENT_USE_NODE24 override", executionContext, glibcInfo, skipNode24Check: false);
            }
            
            if (hasNode24Handler)
            {
                if (useNode24WithHandlerData)
                {
                    return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected for Node24 task with handler knob enabled", executionContext, glibcInfo, skipNode24Check: false);
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
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Upgraded from end-of-life Node version due to EOL policy", executionContext, glibcInfo, skipNode24Check: false);
            }
            
            return null;
        }

        /// <summary>
        /// Determines the appropriate Node version based on glibc compatibility and EOL policy.
        /// </summary>
        /// <param name="skipNode24Check">When true, skips checking Node24 glibc (used when Node24 folder doesn't exist)</param>
        private NodeRunnerInfo DetermineNodeVersionSelection(TaskContext context, bool eolPolicyEnabled, string baseReason, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo, bool skipNode24Check)
        {
            string systemType = context.Container != null ? "container" : "agent";
            
            // Check Node24 availability (skip if Node24 folder doesn't exist)
            if (!skipNode24Check && !glibcInfo.Node24HasGlibcError)
            {
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node24,
                    Reason = baseReason,
                    Warning = null
                };
            }

            // Try Node20
            if (!glibcInfo.Node20HasGlibcError)
            {
                string fallbackReason = skipNode24Check
                    ? $"{baseReason}, falling back to Node20"
                    : $"{baseReason}, fallback to Node20 due to Node24 glibc compatibility issue";
                
                string warning = skipNode24Check
                    ? $"Node24 is not available on this {systemType} platform, using Node20 instead. Please ensure your tasks are compatible with Node20."
                    : StringUtil.Loc("NodeGlibcFallbackWarning", systemType, "Node24", "Node20");

                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node20,
                    Reason = fallbackReason,
                    Warning = warning
                };
            }

            // Node20 also has glibc error, need to fall back to Node16
            executionContext.Debug("[Node24Strategy] Node20 has glibc error, checking EOL policy for Node16 fallback");

            if (eolPolicyEnabled)
            {
                executionContext.Debug("[Node24Strategy] EOL policy is enabled, cannot fall back to Node16");
                string handlerType = context.HandlerData != null ? context.HandlerData.GetType().Name : "UnknownHandlerData";
                
                string errorMessage = skipNode24Check
                    ? $"No compatible Node.js version available. Node24 is not available on this platform and Node20 has glibc compatibility issues. Node16 fallback is blocked by EOL policy. Handler type: {handlerType}. To temporarily disable EOL policy: Set AGENT_RESTRICT_EOL_NODE_VERSIONS=false"
                    : $"No compatible Node.js version available for host execution. Handler type: {handlerType}. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_RESTRICT_EOL_NODE_VERSIONS=false";
                
                throw new NotSupportedException(errorMessage);
            }

            // EOL policy not enabled, fall back to Node16
            string node16Reason = skipNode24Check
                ? $"{baseReason}, Node20 has glibc error, falling back to Node16"
                : $"{baseReason}, fallback to Node16 due to both Node24 and Node20 glibc compatibility issues";
            
            string node16Warning = skipNode24Check
                ? StringUtil.Loc("NodeGlibcFallbackWarning", systemType, "Node20", "Node16")
                : StringUtil.Loc("NodeGlibcFallbackWarning", systemType, "Node24 or Node20", "Node16");

            return new NodeRunnerInfo
            {
                NodePath = null,
                NodeVersion = NodeVersion.Node16,
                Reason = node16Reason,
                Warning = node16Warning
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
    }
}