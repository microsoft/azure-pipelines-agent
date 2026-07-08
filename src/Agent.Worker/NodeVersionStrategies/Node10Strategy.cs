// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Agent.Sdk;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    public sealed class Node10Strategy : INodeVersionStrategy
    {
        public NodeRunnerInfo CanHandle(TaskContext context, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo)
        {
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean();
            string taskName = executionContext.Variables.Get(Constants.Variables.Task.DisplayName) ?? "Unknown Task";

            if (context.EffectiveMaxVersion < 10)
            {
                executionContext.Debug($"[Node10Strategy] EffectiveMaxVersion={context.EffectiveMaxVersion} < 10, skipping");
                return null;
            }

            if (eolPolicyEnabled)
            {
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node10"));
            }

            if (context.HandlerData is Node10HandlerData)
            {
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node10,
                    Reason = "Selected for Node10 task handler",
                    Warning = StringUtil.Loc("NodeEOLRetirementWarning", taskName)
                };
            }

            bool isAlpine = PlatformUtil.RunningOnAlpine;
            if (isAlpine)
            {
                executionContext.Warning(
                    "Using Node10 on Alpine Linux because Node6 is not compatible. " +
                    "Node10 has reached End-of-Life. Please upgrade to Node20 or Node24 for continued support.");

                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node10,
                    Reason = "Selected for Alpine Linux compatibility (Node6 incompatible)",
                    Warning = StringUtil.Loc("NodeEOLRetirementWarning", taskName)
                };
            }

            return null;
        }
        
    }
}
