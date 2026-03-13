// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    public sealed class Node16Strategy : INodeVersionStrategy
    {
        public NodeRunnerInfo CanHandle(TaskContext context, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo)
        {
            bool hasNode16Handler = context.HandlerData is Node16HandlerData;
            bool hasCompatibleHandler = hasNode16Handler || context.HandlerData is Node20_1HandlerData || context.HandlerData is Node24HandlerData;
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean();

            if (eolPolicyEnabled)
            {
                executionContext.Debug(StringUtil.Loc("NodeEOLFallbackBlocked", "Node16"));
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node16"));
            }

            if (hasCompatibleHandler)
            {
                string taskName = executionContext.Variables.Get(Constants.Variables.Task.DisplayName) ?? "Unknown Task";
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node16,
                    Reason = hasNode16Handler ? "Selected for Node16 task handler" : "Fallback to Node16 from higher version handler",
                    Warning = StringUtil.Loc("NodeEOLRetirementWarning", taskName)
                };
            }
            return null;
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
    }
}