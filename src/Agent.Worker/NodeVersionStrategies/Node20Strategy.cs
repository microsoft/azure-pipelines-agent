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
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean();

            string taskName = executionContext.Variables.Get(Constants.Variables.Task.DisplayName) ?? "Unknown Task";

            if (glibcInfo.Node20HasGlibcError)
            {
                executionContext.Debug("[Node20Strategy] Node20 has glibc compatibility issue, skipping");
                return null;
            }

            if (useNode20Globally)
            {
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node20,
                    Reason = "Selected via global AGENT_USE_NODE20_1 override",
                    Warning = null
                };
            }

            if (context.HandlerData is Node20_1HandlerData)
            {
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node20,
                    Reason = "Selected for Node20 task handler",
                    Warning = null
                };
            }

            if (eolPolicyEnabled && context.EffectiveMaxVersion <= 16)
            {
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node20,
                    Reason = "Upgraded from end-of-life Node version due to EOL policy",
                    Warning = StringUtil.Loc("NodeEOLUpgradeWarning", taskName)
                };
            }

            if (context.EffectiveMaxVersion < 20)
            {
                executionContext.Debug($"[Node20Strategy] EffectiveMaxVersion={context.EffectiveMaxVersion} < 20, skipping");
                return null;
            }

            return new NodeRunnerInfo
            {
                NodePath = null,
                NodeVersion = NodeVersion.Node20,
                Reason = "Fallback to Node20",
                Warning = StringUtil.Loc("NodeGlibcFallbackWarning", "agent", "Node24", "Node20")
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