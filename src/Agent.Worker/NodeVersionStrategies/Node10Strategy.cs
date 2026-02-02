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
            bool hasNode10Handler = context.HandlerData is Node10HandlerData;
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean();
            
            if (hasNode10Handler)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected for Node10 task handler", executionContext);
            }

            bool isAlpine = PlatformUtil.RunningOnAlpine;
            if (isAlpine)
            {
                executionContext.Warning(
                    "Using Node10 on Alpine Linux because Node6 is not compatible. " +
                    "Node10 has reached End-of-Life. Please upgrade to Node20 or Node24 for continued support.");
                
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected for Alpine Linux compatibility (Node6 incompatible)", executionContext);
            }

            return null;
        }

        private NodeRunnerInfo DetermineNodeVersionSelection(TaskContext context, bool eolPolicyEnabled, string baseReason, IExecutionContext executionContext)
        {
            string taskName = executionContext.Variables.Get(Constants.Variables.Task.DisplayName) ?? "Unknown Task";
            if (eolPolicyEnabled)
            {
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node10"));
            }
            
            return new NodeRunnerInfo
            {
                NodePath = null,
                NodeVersion = NodeVersion.Node10,
                Reason = baseReason,
                Warning = StringUtil.Loc("NodeEOLRetirementWarning", taskName)
            };
        }
        
    }
}
