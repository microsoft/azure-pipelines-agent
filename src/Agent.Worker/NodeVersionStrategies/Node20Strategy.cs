// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    public sealed class Node20Strategy : INodeVersionStrategy
    {
        public NodeRunnerInfo CanHandle(TaskContext context, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo)
        {
            bool useNode20Globally = AgentKnobs.UseNode20_1.GetValue(executionContext).AsBoolean();
            bool hasNode20Handler = context.HandlerData is Node20_1HandlerData;
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean();
            
            if (useNode20Globally)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected via global AGENT_USE_NODE20_1 override", executionContext, glibcInfo);
            }
            
            if (hasNode20Handler)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected for Node20 task handler", executionContext, glibcInfo);
            }
            
            if (eolPolicyEnabled)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Upgraded from end-of-life Node version due to EOL policy", executionContext, glibcInfo, isUpgradeScenario: true);
            }
            
            return null;
        }

        private NodeRunnerInfo DetermineNodeVersionSelection(TaskContext context, bool eolPolicyEnabled, string baseReason, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo, bool isUpgradeScenario = false)
        {
            string taskName = executionContext.Variables.Get(Constants.Variables.Task.DisplayName) ?? "Unknown Task";
            string upgradeWarning = isUpgradeScenario ? StringUtil.Loc("NodeEOLUpgradeWarning", taskName) : null;
            
            if (!glibcInfo.Node20HasGlibcError)
            {
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node20,
                    Reason = baseReason,
                    Warning = upgradeWarning
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
                Warning = StringUtil.Loc("NodeEOLRetirementWarning", taskName)
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
    }
}